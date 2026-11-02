using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MeoVision.Core;
using MeoVision.Core.Devices;
using MeoVision.Core.Localization;
using Microsoft.Extensions.Logging;

namespace MeoVision.App.Comm.Plc;

/// <summary>Thiết lập của kênh <c>McPlc</c> trong <c>machine.json</c>.</summary>
public sealed record PlcChannelOptions(string Host, int Port, int BitStart, int WordStart,
    TimeSpan Poll, TimeSpan Timeout, int MaxOutputs)
{
    private static readonly Regex DevicePattern = new("^([MD])(\\d{1,6})$", RegexOptions.Compiled);

    public static PlcChannelOptions Read(ChannelConfig channel, List<Msg> problems)
    {
        Guard.ThrowIfNull(channel, nameof(channel));
        SettingsReader s = new(new DeviceConfig { Role = channel.Id, Settings = channel.Settings },
            problems);
        string host = s.Required("host");
        if (host.Length > 0 && !IPAddress.TryParse(host, out _))
        {
            problems.Add(Msg.Of("machine.setting.badValue", channel.Id, "host", host,
                "192.168.1.10"));
        }
        int port = s.Int("port", -1, 1, 65535);
        if (port < 0)
        {
            problems.Add(Msg.Of("machine.setting.missing", channel.Id, "port"));
        }
        return new PlcChannelOptions(host, Math.Max(port, 0),
            Device(s.Optional("bitStart", "M100"), 'M', channel.Id, "bitStart", problems),
            Device(s.Optional("wordStart", "D100"), 'D', channel.Id, "wordStart", problems),
            TimeSpan.FromMilliseconds(s.Int("pollMs", 20, 5, 1000)),
            TimeSpan.FromMilliseconds(s.Int("timeoutMs", 1000, 100, 10000)),
            s.Int("maxOutputs", 8, 1, 16));
    }

    private static int Device(string text, char kind, string channel, string key,
        List<Msg> problems)
    {
        Match m = DevicePattern.Match(text.ToUpperInvariant());
        if (!m.Success || m.Groups[1].Value[0] != kind)
        {
            problems.Add(Msg.Of("machine.setting.badValue", channel, key, text, kind + "100"));
            return 0;
        }
        return int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
    }
}

/// <summary>
/// Kênh PLC Mitsubishi qua MC 3E (Chương 21, mục 21.4). Vùng bắt tay mỗi kênh một vùng, tên tín
/// hiệu theo NDM của Cognex. PLC đặt mã lệnh/sản phẩm/serial rồi bật Trigger; vision bật
/// TriggerAck, chạy lệnh qua CHÍNH <see cref="CommandService"/> như kênh TCP, ghi kết quả, bật
/// InspectionComplete. PLC tắt Trigger → vision tắt các bit của mình. Bảng địa chỉ: <see
/// cref="Bit"/>,
/// <see cref="Word"/>.
/// </summary>
public sealed class PlcChannel : IChannel
{
    /// <summary>Độ lệch bit tính từ <c>bitStart</c>.</summary>
    public static class Bit
    {
        public const int Trigger = 0;             // PLC → vision
        public const int TriggerAck = 1;          // vision → PLC
        public const int InspectionComplete = 2;  // vision → PLC
        public const int ResultsValid = 3;        // vision → PLC: OK hoặc NG (đo được)
        public const int Online = 4;              // vision → PLC
        public const int Error = 5;               // vision → PLC: ERR (không đo được)
        public const int Heartbeat = 6;           // vision → PLC: đảo mỗi giây
        public const int Bypass = 7;              // vision → PLC: lệnh này KHÔNG được kiểm (D1)
        public const int Count = 8;
    }

    /// <summary>
    /// Độ lệch word tính từ <c>wordStart</c>. Chuỗi ASCII, mỗi word hai ký tự, byte thấp trước.
    /// </summary>
    public static class Word
    {
        public const int Code = 0;          // 4 word = 8 ký tự   (PLC → vision)
        public const int Product = 4;       // 8 word = 16 ký tự
        public const int Serial = 12;       // 10 word = 20 ký tự
        public const int RequestCount = 22;
        public const int Status = 30;       // 1 OK · 2 NG · 3 ERR · 4 BYPASS   (vision → PLC)
        public const int Reason = 31;       // 4 word — giữ chỗ, luôn 0 (lý do không gửi máy, DESIGN_LOG [29])
        public const int Outputs = 40;      // mỗi giá trị REAL 32 bit = 2 word, word thấp trước
    }

    private readonly PlcChannelOptions _options;
    private bool[]? _lastBits;

    /// <summary>
    /// Vùng bit bắt tay đọc ở lần quét gần nhất (theo <see cref="Bit"/>) — màn Giao tiếp xem trực tiếp
    /// Trigger/Ack/Complete khi nghiệm thu (DESIGN_LOG [28] D2). Rỗng khi chưa nối.
    /// </summary>
    public IReadOnlyList<bool> LastBits =>
        Connections == 0 ? Array.Empty<bool>() : (IReadOnlyList<bool>?)Volatile.Read(ref _lastBits) ?? Array.Empty<bool>();
    private readonly LineHandler _handler;
    private readonly Func<bool> _online;
    private readonly ILogger _logger;
    private readonly McProtocolClient _plc;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private long _lastTicks;
    private int _connected;

    public PlcChannel(string id, PlcChannelOptions options, LineHandler handler, Func<bool> online,
        ILogger logger)
    {
        Guard.ThrowIfNull(id, nameof(id));
        Guard.ThrowIfNull(options, nameof(options));
        Guard.ThrowIfNull(handler, nameof(handler));
        Guard.ThrowIfNull(online, nameof(online));
        Guard.ThrowIfNull(logger, nameof(logger));
        Id = id;
        _options = options;
        _handler = handler;
        _online = online;
        _logger = logger;
        _plc = new McProtocolClient(options.Host, options.Port, options.Timeout);
    }

    public string Id { get; }

    public string Description =>
        $"MC 3E {_options.Host}:{_options.Port} M{_options.BitStart} D{_options.WordStart}";

    public int Connections => Volatile.Read(ref _connected);

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
        _loop = RunAsync(_cts.Token);
        return Task.CompletedTask;
    }

    public void Stop()
    {
        _cts?.Cancel();
        try
        {
            _loop?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
            // vòng lặp kết thúc vì huỷ — đúng ý
        }
    }

    public void Dispose()
    {
        Stop();
        _plc.Dispose();
        _cts?.Dispose();
    }

    private async Task RunAsync(CancellationToken ct)
    {
        TimeSpan retry = TimeSpan.FromSeconds(1);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await _plc.ConnectAsync(ct).ConfigureAwait(false);
                SetConnected(true);
                _logger.LogInformation("Channel {Channel} connected: {Plc}", Id, Description);
                retry = TimeSpan.FromSeconds(1);
                await PollAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex) when (ex is McProtocolException or
                                           System.Net.Sockets.SocketException or
                                           System.IO.IOException or ObjectDisposedException)
            {
                // Mất PLC: thử lại với khoảng chờ tăng dần (như watchdog thiết bị, Chương 18).
                SetConnected(false);
                _logger.LogWarning("Channel {Channel}: {Error} — retry in {Seconds} s", Id,
                    ex.Message, retry.TotalSeconds);
                try
                {
                    await Task.Delay(retry, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                retry = TimeSpan.FromSeconds(Math.Min(retry.TotalSeconds * 2, 30));
            }
        }
        SetConnected(false);
    }

    private async Task PollAsync(CancellationToken ct)
    {
        int b = _options.BitStart, w = _options.WordStart;
        bool acked = false, beat = false;
        DateTime nextBeat = DateTime.MinValue;
        // Lúc mới nối: xoá bit của vision còn sót từ lần chạy trước (phần mềm bị kill giữa chu
        // kỳ).
        await _plc.WriteBitsAsync(McDevice.M, b + Bit.TriggerAck,
            new[] { false, false, false, _online(), false }, ct).ConfigureAwait(false);
        await _plc.WriteBitsAsync(McDevice.M, b + Bit.Bypass, new[] { false }, ct).ConfigureAwait(false);
        while (!ct.IsCancellationRequested)
        {
            bool[] bits = await _plc.ReadBitsAsync(McDevice.M, b, Bit.Count, ct)
                .ConfigureAwait(false);
            Volatile.Write(ref _lastBits, bits);
            bool trigger = bits[Bit.Trigger];
            if (trigger && !acked)
            {
                // Sườn lên của Trigger: một chu kỳ. Báo đã nhận TRƯỚC khi làm, xoá kết quả cũ.
                ushort[] request = await _plc.ReadWordsAsync(McDevice.D, w, Word.RequestCount, ct)
                    .ConfigureAwait(false);
                await _plc.WriteBitsAsync(McDevice.M, b + Bit.TriggerAck,
                    new[] { true, false, false }, ct).ConfigureAwait(false);
                acked = true;
                Interlocked.Exchange(ref _lastTicks, DateTime.UtcNow.Ticks);
                string line = string.Join(",", Text(request, Word.Code, 4),
                    Text(request, Word.Product, 8), Text(request, Word.Serial, 10));
                string reply = await _handler(Id, line, ct).ConfigureAwait(false);
                await WriteResultAsync(reply, ct).ConfigureAwait(false);
            }
            else if (!trigger && acked)
            {
                // PLC đã đọc xong và tắt Trigger: dọn bit của vision, sẵn sàng chu kỳ sau.
                await _plc.WriteBitsAsync(McDevice.M, b + Bit.TriggerAck,
                    new[] { false, false, false }, ct).ConfigureAwait(false);
                await _plc.WriteBitsAsync(McDevice.M, b + Bit.Bypass, new[] { false }, ct)
                    .ConfigureAwait(false);
                acked = false;
            }

            if (DateTime.UtcNow >= nextBeat)
            {
                beat = !beat;
                nextBeat = DateTime.UtcNow.AddSeconds(1);
                await _plc.WriteBitsAsync(McDevice.M, b + Bit.Online, new[] { _online() }, ct)
                    .ConfigureAwait(false);
                await _plc.WriteBitsAsync(McDevice.M, b + Bit.Heartbeat, new[] { beat }, ct)
                    .ConfigureAwait(false);
            }
            await Task.Delay(_options.Poll, ct).ConfigureAwait(false);
        }
    }

    /// <summary>Chuyển câu trả lời dạng chuỗi của CommandService sang thanh ghi.</summary>
    private async Task WriteResultAsync(string reply, CancellationToken ct)
    {
        string[] f = reply.Split(',');
        string status = f.Length > 2 ? f[2] : MessageProtocol.Err;
        ushort code = status switch
        {
            MessageProtocol.Ok => 1, MessageProtocol.Ng => 2, MessageProtocol.Bypass => 4, _ => 3,
        };
        List<ushort> words = new() { code };
        words.AddRange(Ascii(f.Length > 3 ? f[3] : string.Empty, 4));
        // Giữa Reason (4 word) và Outputs: để trống cho dễ nhìn trên màn hình thiết bị của PLC.
        while (words.Count < Word.Outputs - Word.Status)
        {
            words.Add(0);
        }
        for (int i = 0; i < _options.MaxOutputs; i++)
        {
            // Số gửi PLC là REAL 32 bit; giá trị không phải số (mã đọc được) → NaN, cần kênh TCP.
            float value = 4 + i < f.Length && float.TryParse(f[4 + i], NumberStyles.Float,
                CultureInfo.InvariantCulture, out float v) ? v : float.NaN;
            byte[] raw = BitConverter.GetBytes(value);
            words.Add((ushort)(raw[0] | (raw[1] << 8)));
            words.Add((ushort)(raw[2] | (raw[3] << 8)));
        }
        await _plc.WriteWordsAsync(McDevice.D, _options.WordStart + Word.Status, words, ct)
            .ConfigureAwait(false);
        // Kết quả ghi XONG rồi mới bật InspectionComplete: PLC thấy bit là đọc được ngay.
        bool measured = code is 1 or 2;
        await _plc.WriteBitsAsync(McDevice.M, _options.BitStart + Bit.Bypass, new[] { code == 4 }, ct)
            .ConfigureAwait(false);
        await _plc.WriteBitsAsync(McDevice.M, _options.BitStart + Bit.InspectionComplete,
            new[] { true, measured, _online(), !measured && code != 4 }, ct).ConfigureAwait(false);
    }

    private static string Text(ushort[] words, int offset, int count)
    {
        StringBuilder sb = new(count * 2);
        for (int i = offset; i < offset + count; i++)
        {
            sb.Append((char)(words[i] & 0xFF)).Append((char)(words[i] >> 8));
        }
        return sb.ToString().TrimEnd('\0', ' ');
    }

    private static IEnumerable<ushort> Ascii(string text, int wordCount)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(text.PadRight(wordCount * 2, '\0'));
        return Enumerable.Range(0, wordCount)
            .Select(i => (ushort)(bytes[2 * i] | (bytes[2 * i + 1] << 8)));
    }

    private void SetConnected(bool connected)
    {
        if (Interlocked.Exchange(ref _connected, connected ? 1 : 0) != (connected ? 1 : 0))
        {
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
