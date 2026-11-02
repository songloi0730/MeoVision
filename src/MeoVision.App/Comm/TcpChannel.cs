using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MeoVision.Core;
using MeoVision.Core.Devices;
using MeoVision.Core.Localization;
using Microsoft.Extensions.Logging;

namespace MeoVision.App.Comm;

/// <summary>Thiết lập của kênh <c>TcpServer</c> trong <c>machine.json</c>.</summary>
public sealed record TcpChannelOptions(IPAddress Host, int Port,
    IReadOnlyList<IPAddress> AllowedClients, string Terminator, int MaxClients)
{
    /// <summary>
    /// Đọc và kiểm thiết lập; lỗi gom vào <paramref name="problems"/> (Chương 18, mục 18.2.3).
    /// </summary>
    public static TcpChannelOptions Read(ChannelConfig channel, List<Msg> problems)
    {
        Guard.ThrowIfNull(channel, nameof(channel));
        SettingsReader s = new(new DeviceConfig { Role = channel.Id, Settings = channel.Settings },
            problems);
        string hostText = s.Optional("host", "127.0.0.1");
        if (!IPAddress.TryParse(hostText, out IPAddress? host))
        {
            problems.Add(Msg.Of("machine.setting.badValue", channel.Id, "host", hostText,
                "127.0.0.1, 0.0.0.0, 192.168.1.20"));
            host = IPAddress.Loopback;
        }
        int port = s.Int("port", -1, 0, 65535);
        if (port < 0)
        {
            problems.Add(Msg.Of("machine.setting.missing", channel.Id, "port"));
        }
        List<IPAddress> allowed = new();
        foreach (string item in s.Optional("allowedClients", string.Empty)
                     .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (IPAddress.TryParse(item.Trim(), out IPAddress? ip))
            {
                allowed.Add(ip);
            }
            else
            {
                problems.Add(Msg.Of("machine.setting.badValue", channel.Id, "allowedClients",
                    item.Trim(), "192.168.1.30"));
            }
        }
        string terminator = s.Optional("terminator", "CR").ToUpperInvariant();
        if (terminator is not ("CR" or "LF" or "CRLF"))
        {
            problems.Add(Msg.Of("machine.setting.badValue", channel.Id, "terminator", terminator,
                "CR, LF, CRLF"));
            terminator = "CR";
        }
        return new TcpChannelOptions(host!, Math.Max(port, 0), allowed,
            terminator switch { "LF" => "\n", "CRLF" => "\r\n", _ => "\r" },
            s.Int("maxClients", 4, 1, 16));
    }
}

/// <summary>
/// Kênh TCP server (Chương 21, mục 21.3): máy (PC control hoặc PLC có TCP) nối vào, gửi một dòng,
/// nhận một dòng. Nhận cả CR, LF hay CRLF làm ký tự kết thúc (mỗi hãng một kiểu); trả lời bằng ký
/// tự đã khai. Mỗi bản tin xử lý riêng — hai lệnh của hai camera trên cùng kết nối chạy song
/// song, câu trả lời mang mã lệnh + serial để máy ghép đúng cặp.
/// </summary>
public sealed class TcpChannel : IChannel
{
    /// <summary>
    /// Một dòng dài hơn thế là rác (gửi nhầm cổng, dữ liệu nhị phân) — đóng kết nối.
    /// </summary>
    public const int MaxLineBytes = 1024;

    // ISO-8859-1: mỗi byte thành đúng một ký tự, byte lạ (> 0x7E) không bị đổi thành '?' hợp lệ —
    // MessageProtocol thấy và trả BAD_FORMAT.
    private static readonly Encoding Wire = Encoding.GetEncoding(28591);

    private readonly TcpChannelOptions _options;
    private readonly LineHandler _handler;
    private readonly ILogger _logger;
    private readonly List<TcpClient> _clients = new();
    private readonly object _sync = new();
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private long _lastTicks;

    public TcpChannel(string id, TcpChannelOptions options, LineHandler handler, ILogger logger)
    {
        Guard.ThrowIfNull(id, nameof(id));
        Guard.ThrowIfNull(options, nameof(options));
        Guard.ThrowIfNull(handler, nameof(handler));
        Guard.ThrowIfNull(logger, nameof(logger));
        Id = id;
        _options = options;
        _handler = handler;
        _logger = logger;
    }

    public string Id { get; }

    public string Description => $"TCP {_options.Host}:{Port}";

    /// <summary>Cổng đang nghe (khai 0 thì hệ điều hành chọn — dùng trong test).</summary>
    public int Port => (_listener?.LocalEndpoint as IPEndPoint)?.Port ?? _options.Port;

    public int Connections
    {
        get { lock (_sync) { return _clients.Count; } }
    }

    public DateTime? LastMessageUtc
    {
        get
        {
            long ticks = Interlocked.Read(ref _lastTicks);
            return ticks == 0 ? null : new DateTime(ticks, DateTimeKind.Utc);
        }
    }

    public event EventHandler? StatusChanged;

    public Task StartAsync(CancellationToken ct)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _listener = new TcpListener(_options.Host, _options.Port);
        _listener.Start();   // cổng đang bị chiếm → SocketException cho bước khởi động báo
        _logger.LogInformation("Channel {Channel} listening on {Endpoint}", Id, Description);
        _ = AcceptLoopAsync(_listener, _cts.Token);
        return Task.CompletedTask;
    }

    public void Stop()
    {
        _cts?.Cancel();
        _listener?.Stop();
        lock (_sync)
        {
            foreach (TcpClient client in _clients)
            {
                client.Close();
            }
            _clients.Clear();
        }
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is ObjectDisposedException or SocketException or
                                           InvalidOperationException)
            {
                return;   // Stop() đóng listener
            }
            IPAddress remote = ((IPEndPoint)client.Client.RemoteEndPoint).Address;
            // Hai máy tính: chỉ nhận đúng máy điều khiển đã khai — một laptop cắm nhầm mạng không
            // được gửi lệnh "SW" đổi sản phẩm.
            bool allowed = _options.AllowedClients.Count == 0 ||
                           _options.AllowedClients.Any(a => a.Equals(remote));
            if (!allowed || Connections >= _options.MaxClients)
            {
                _logger.LogWarning("Channel {Channel}: refused {Remote} ({Why})", Id, remote,
                    allowed ? "too many clients" : "not in allowedClients");
                client.Close();
                continue;
            }
            client.NoDelay = true;   // bản tin ngắn: không chờ gom gói (Nagle) thêm vài chục ms
            lock (_sync)
            {
                _clients.Add(client);
            }
            _logger.LogInformation("Channel {Channel}: client {Remote} connected", Id, remote);
            StatusChanged?.Invoke(this, EventArgs.Empty);
            _ = ServeAsync(client, remote, ct);
        }
    }

    private async Task ServeAsync(TcpClient client, IPAddress remote, CancellationToken ct)
    {
        SemaphoreSlim writeGate = new(1, 1);
        List<Task> pending = new();
        try
        {
            NetworkStream stream = client.GetStream();
            byte[] buffer = new byte[512];
            List<byte> line = new();
            while (!ct.IsCancellationRequested)
            {
                int n = await stream.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false);
                if (n == 0)
                {
                    break;   // máy đóng kết nối
                }
                for (int i = 0; i < n; i++)
                {
                    byte b = buffer[i];
                    if (b is (byte)'\r' or (byte)'\n')
                    {
                        if (line.Count > 0)
                        {
                            string text = Wire.GetString(line.ToArray());
                            line.Clear();
                            pending.RemoveAll(t => t.IsCompleted);
                            pending.Add(RespondAsync(stream, writeGate, text, ct));
                        }
                        continue;
                    }
                    line.Add(b);
                    if (line.Count > MaxLineBytes)
                    {
                        _logger.LogWarning(
                            "Channel {Channel}: {Remote} sent a line over {Max} bytes",
                            Id, remote, MaxLineBytes);
                        return;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is System.IO.IOException or ObjectDisposedException or
                                       OperationCanceledException or SocketException)
        {
            // rút cáp, máy đóng đột ngột, đang tắt phần mềm — đều kết thúc phiên này
        }
        finally
        {
            await Task.WhenAll(pending).ConfigureAwait(false);
            lock (_sync)
            {
                _clients.Remove(client);
            }
            client.Close();
            writeGate.Dispose();
            _logger.LogInformation("Channel {Channel}: client {Remote} disconnected", Id, remote);
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task RespondAsync(NetworkStream stream, SemaphoreSlim writeGate, string line,
        CancellationToken ct)
    {
        Interlocked.Exchange(ref _lastTicks, DateTime.UtcNow.Ticks);
        string reply;
        try
        {
            reply = await _handler(Id, line, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;   // đang tắt phần mềm
        }
        catch (Exception ex)
        {
            // Lỗi lập trình trong xử lý lệnh: máy vẫn phải nhận một câu trả lời, không chờ hết
            // hạn.
            _logger.LogError(ex, "Channel {Channel}: handling \"{Line}\" failed", Id, line);
            reply = MessageProtocol.Reply(line.Split(',')[0].Trim(), string.Empty,
                MessageProtocol.Err, "INTERNAL", Array.Empty<string>(), 0);
        }
        byte[] bytes = Wire.GetBytes(reply + _options.Terminator);
        try
        {
            await writeGate.WaitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        try
        {
            // Hai câu trả lời không được đan byte vào nhau trên cùng một kết nối.
            await stream.WriteAsync(bytes, 0, bytes.Length, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is System.IO.IOException or ObjectDisposedException or
                                       OperationCanceledException)
        {
            _logger.LogWarning("Channel {Channel}: reply \"{Reply}\" lost — client gone", Id,
                reply);
        }
        finally
        {
            writeGate.Release();
        }
    }
}
