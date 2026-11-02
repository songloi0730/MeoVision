using System;
using System.IO;
using System.IO.Ports;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MeoVision.Core;

namespace MeoVision.App.Devices.Lights;

/// <summary>
/// Kênh gửi/nhận một dòng ASCII — tách giao thức đèn khỏi đường truyền (RS232 hay TCP), nên một
/// lớp đèn chạy được trên cả hai và test được bằng bộ điều khiển giả qua TCP (Chương 18, mục 18.6).
/// </summary>
public interface ILineChannel : IDisposable
{
    void Open();

    void Close();

    /// <summary>Gửi <paramref name="line"/> (đã gồm ký tự kết thúc nếu giao thức cần).</summary>
    Task SendAsync(string line, CancellationToken ct);

    /// <summary>
    /// Đọc tới khi gặp <paramref name="terminator"/>; trả về phần trước terminator.
    /// </summary>
    Task<string> ReadUntilAsync(string terminator, CancellationToken ct);
}

/// <summary>Cổng nối tiếp (RS232/RS485 qua bộ chuyển đổi).</summary>
public sealed class SerialLineChannel : ILineChannel
{
    private readonly string _portName;
    private readonly int _baudRate;
    private SerialPort? _port;

    public SerialLineChannel(string portName, int baudRate)
    {
        Guard.ThrowIfNull(portName, nameof(portName));
        _portName = portName;
        _baudRate = baudRate;
    }

    public void Open()
    {
        Close();
        // Tạo cổng lúc mở, không lúc khởi tạo: kiểm cấu hình không được giữ tài nguyên nào.
        _port = new SerialPort(_portName, _baudRate, Parity.None, 8, StopBits.One)
        {
            Encoding = Encoding.ASCII,
            ReadTimeout = SerialPort.InfiniteTimeout,   // thời hạn do DeviceManager quyết định
        };
        _port.Open();
    }

    public void Close()
    {
        _port?.Dispose();
        _port = null;
    }

    public Task SendAsync(string line, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Port.DiscardInBuffer();   // bỏ phản hồi muộn của lệnh trước — không đọc nhầm
        Port.Write(line);
        return Task.CompletedTask;
    }

    public Task<string> ReadUntilAsync(string terminator, CancellationToken ct) =>
        LineReader.ReadUntilAsync(Port.BaseStream, terminator, ct);

    public void Dispose() => Close();

    private SerialPort Port =>
        _port ?? throw new InvalidOperationException("Channel is not open");
}

/// <summary>Bộ điều khiển đèn có cổng Ethernet (TCP).</summary>
public sealed class TcpLineChannel : ILineChannel
{
    private readonly string _host;
    private readonly int _port;
    private TcpClient? _client;

    public TcpLineChannel(string host, int port)
    {
        Guard.ThrowIfNull(host, nameof(host));
        _host = host;
        _port = port;
    }

    public void Open()
    {
        Close();
        _client = new TcpClient { NoDelay = true };
        _client.Connect(_host, _port);
    }

    public void Close()
    {
        _client?.Dispose();
        _client = null;
    }

    public async Task SendAsync(string line, CancellationToken ct)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(line);
        await Stream.WriteAsync(bytes, 0, bytes.Length, ct).ConfigureAwait(false);
    }

    public Task<string> ReadUntilAsync(string terminator, CancellationToken ct) =>
        LineReader.ReadUntilAsync(Stream, terminator, ct);

    public void Dispose() => Close();

    private NetworkStream Stream =>
        _client?.GetStream() ?? throw new InvalidOperationException("Channel is not open");
}

internal static class LineReader
{
    /// <summary>
    /// Đọc từng byte tới terminator. Phía bên kia đóng kết nối (đọc được 0 byte) là LỖI, không
    /// phải "chuỗi rỗng" — nếu không, vòng đọc sẽ quay mãi trên một socket đã chết.
    /// </summary>
    public static async Task<string> ReadUntilAsync(Stream stream, string terminator,
        CancellationToken ct)
    {
        StringBuilder sb = new();
        byte[] one = new byte[1];
        while (true)
        {
            int n = await stream.ReadAsync(one, 0, 1, ct).ConfigureAwait(false);
            if (n == 0)
            {
                throw new IOException("Connection closed by the device");
            }
            sb.Append((char)one[0]);
            if (sb.Length >= terminator.Length &&
                sb.ToString(sb.Length - terminator.Length, terminator.Length) == terminator)
            {
                return sb.ToString(0, sb.Length - terminator.Length);
            }
        }
    }
}
