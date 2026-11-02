using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MeoVision.Core;
using MeoVision.Core.Contracts;
using Microsoft.Extensions.Logging;

namespace MeoVision.WorkStation.Demo;

// Bản thật của IPlcCommunication (DESIGN_LOG.md mục [6]) — ASCII line-based qua TCP, Vision là
// client chủ động ConnectAsync tới PLC/gateway (đóng vai server) tại Host:Port. Chọn text thay vì
// nhị phân vì mục đích chính là để MeoVision.PlcTestReceiver hiển thị được ngay trên console, không
// cần công cụ giải mã riêng — xem bảng phương án trong design-note.
public sealed class TcpPlcCommunication : IPlcCommunication
{
    private readonly TcpPlcCommunicationOptions _options;
    private readonly ILogger<TcpPlcCommunication> _logger;
    private TcpClient? _client;
    private StreamReader? _reader;
    private StreamWriter? _writer;

    public TcpPlcCommunication(TcpPlcCommunicationOptions options, ILogger<TcpPlcCommunication> logger)
    {
        Guard.ThrowIfNull(options, nameof(options));
        Guard.ThrowIfNull(logger, nameof(logger));
        _options = options;
        _logger = logger;
    }

    public bool IsConnected => _client?.Connected ?? false;

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        _logger.LogDebug("Starting {Method} host={Host} port={Port}",
            nameof(ConnectAsync), _options.Host, _options.Port);

        TcpClient client = new();
        Task connectTask = client.ConnectAsync(_options.Host, _options.Port);

        // net48 TcpClient.ConnectAsync(host, port) không nhận CancellationToken — đua với
        // Task.Delay để có timeout/huỷ được, cùng tinh thần CS03 nhưng áp cho tầng transport
        // thay vì một lệnh gọi SDK có sẵn CancellationToken.
        Task delayTask = Task.Delay(_options.ConnectTimeoutMs, ct);
        Task completedTask = await Task.WhenAny(connectTask, delayTask).ConfigureAwait(false);
        if (completedTask == delayTask)
        {
            client.Close();
            ct.ThrowIfCancellationRequested();
            throw new TimeoutException(
                $"Không kết nối được PLC tại {_options.Host}:{_options.Port} sau {_options.ConnectTimeoutMs}ms — " +
                "kiểm tra MeoVision.PlcTestReceiver (hoặc PLC thật) đã lắng nghe tại địa chỉ này chưa.");
        }

        await connectTask.ConfigureAwait(false); // ném lại lỗi kết nối thật nếu có (vd: bị từ chối)

        _client = client;
        NetworkStream stream = client.GetStream();
        _reader = new StreamReader(stream, Encoding.ASCII);
        _writer = new StreamWriter(stream, Encoding.ASCII) { AutoFlush = true, NewLine = "\n" };

        _logger.LogInformation("Đã kết nối PLC tại {Host}:{Port}", _options.Host, _options.Port);
    }

    public Task DisconnectAsync(CancellationToken ct = default)
    {
        _reader?.Dispose();
        _writer?.Dispose();
        _client?.Close();
        _reader = null;
        _writer = null;
        _client = null;
        return Task.CompletedTask;
    }

    public async Task WaitForTriggerAsync(CancellationToken ct = default)
    {
        string line = await ReadLineAsync(ct).ConfigureAwait(false);
        if (!string.Equals(line, "TRIGGER", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Kỳ vọng 'TRIGGER' từ PLC, nhận '{line}'.");
        }
    }

    public Task SetBusyAsync(bool busy, CancellationToken ct = default) =>
        WriteLineAsync(busy ? "BUSY 1" : "BUSY 0", ct);

    public Task SetDoneAsync(bool done, CancellationToken ct = default) =>
        WriteLineAsync(done ? "DONE 1" : "DONE 0", ct);

    public async Task WaitForAckAsync(CancellationToken ct = default)
    {
        string line = await ReadLineAsync(ct).ConfigureAwait(false);
        if (!string.Equals(line, "ACK", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Kỳ vọng 'ACK' từ PLC, nhận '{line}'.");
        }
    }

    private async Task<string> ReadLineAsync(CancellationToken ct)
    {
        StreamReader? reader = _reader;
        TcpClient? client = _client;
        if (reader is null || client is null)
        {
            throw new InvalidOperationException("Chưa ConnectAsync — gọi trước khi đọc tín hiệu PLC.");
        }

        Task<string?> readTask = reader.ReadLineAsync();

        // net48 StreamReader.ReadLineAsync() không nhận CancellationToken — đóng socket khi bị huỷ
        // để buộc read task ném exception thay vì treo vô thời hạn (cùng ý tưởng CS03, áp cho
        // blocking Stream read thay vì một lệnh gọi SDK có sẵn CancellationToken).
        using (ct.Register(static state => ((TcpClient)state!).Close(), client))
        {
            string? line;
            try
            {
                line = await readTask.ConfigureAwait(false);
            }
            catch (Exception ex) when (ct.IsCancellationRequested && ex is IOException or ObjectDisposedException)
            {
                throw new OperationCanceledException("Đọc tín hiệu PLC bị huỷ.", ex, ct);
            }

            return line
                ?? throw new InvalidOperationException("Kết nối PLC đã đóng (nhận EOF) trong lúc chờ tín hiệu.");
        }
    }

    private async Task WriteLineAsync(string message, CancellationToken ct)
    {
        StreamWriter? writer = _writer;
        if (writer is null)
        {
            throw new InvalidOperationException("Chưa ConnectAsync — gọi trước khi gửi tín hiệu PLC.");
        }

        ct.ThrowIfCancellationRequested();
        await writer.WriteLineAsync(message).ConfigureAwait(false);
    }

    public void Dispose()
    {
        _reader?.Dispose();
        _writer?.Dispose();
        _client?.Close();
    }
}
