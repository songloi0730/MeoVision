using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using MeoVision.Core;

namespace MeoVision.App.Comm.Plc;

/// <summary>Mã thiết bị trong khung MC 3E nhị phân.</summary>
public enum McDevice : byte
{
    /// <summary>Thanh ghi dữ liệu D (word).</summary>
    D = 0xA8,

    /// <summary>Rơ-le nội M (bit).</summary>
    M = 0x90,
}

/// <summary>Lỗi giao thức MC: PLC trả mã kết thúc khác 0, khung sai, hoặc quá thời hạn.</summary>
public sealed class McProtocolException : Exception
{
    public McProtocolException(string message, int endCode = 0)
        : base(message)
    {
        EndCode = endCode;
    }

    public McProtocolException()
        : base("MC protocol error")
    {
    }

    public McProtocolException(string message, Exception inner)
        : base(message, inner)
    {
    }

    /// <summary>Mã kết thúc PLC trả về (0 = không có, ví dụ quá thời hạn).</summary>
    public int EndCode { get; }
}

/// <summary>
/// Client giao thức MC (SLMP) khung 3E nhị phân của PLC Mitsubishi qua TCP (Chương 21, mục 21.4):
/// đọc/ghi liên tiếp word (D) và bit (M). Mỗi lúc một yêu cầu; mỗi yêu cầu có thời hạn — quá hạn
/// thì đóng kết nối, kênh PLC tự nối lại.
/// </summary>
public sealed class McProtocolClient : IDisposable
{
    public const ushort CommandBatchRead = 0x0401;
    public const ushort CommandBatchWrite = 0x1401;
    public const ushort SubcommandWord = 0x0000;
    public const ushort SubcommandBit = 0x0001;

    /// <summary>Thời gian PLC được phép xử lý, đơn vị 250 ms: 4 = 1 s.</summary>
    private const ushort MonitoringTimer = 4;

    private readonly string _host;
    private readonly int _port;
    private readonly TimeSpan _timeout;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private TcpClient? _client;
    private NetworkStream? _stream;

    public McProtocolClient(string host, int port, TimeSpan timeout)
    {
        Guard.ThrowIfNull(host, nameof(host));
        _host = host;
        _port = port;
        _timeout = timeout;
    }

    public bool Connected => _client?.Connected == true;

    public async Task ConnectAsync(CancellationToken ct)
    {
        Close();
        TcpClient client = new() { NoDelay = true };
        Task connect = client.ConnectAsync(_host, _port);
        if (await Task.WhenAny(connect, Task.Delay(_timeout, ct)).ConfigureAwait(false) != connect)
        {
            client.Close();
            throw new McProtocolException($"Connect to {_host}:{_port} timed out");
        }
        await connect.ConfigureAwait(false);
        _client = client;
        _stream = client.GetStream();
    }

    public async Task<ushort[]> ReadWordsAsync(McDevice device, int start, int count,
        CancellationToken ct)
    {
        byte[] data = await RequestAsync(CommandBatchRead, SubcommandWord, device, start, count,
            Array.Empty<byte>(), ct).ConfigureAwait(false);
        if (data.Length != count * 2)
        {
            throw new McProtocolException($"Expected {count * 2} data bytes, got {data.Length}");
        }
        ushort[] words = new ushort[count];
        for (int i = 0; i < count; i++)
        {
            // Mỗi word: byte thấp trước.
            words[i] = (ushort)(data[2 * i] | (data[2 * i + 1] << 8));
        }
        return words;
    }

    public Task WriteWordsAsync(McDevice device, int start, IReadOnlyList<ushort> values,
        CancellationToken ct)
    {
        Guard.ThrowIfNull(values, nameof(values));
        byte[] data = new byte[values.Count * 2];
        for (int i = 0; i < values.Count; i++)
        {
            data[2 * i] = (byte)(values[i] & 0xFF);
            data[2 * i + 1] = (byte)(values[i] >> 8);
        }
        return RequestAsync(CommandBatchWrite, SubcommandWord, device, start, values.Count, data,
            ct);
    }

    public async Task<bool[]> ReadBitsAsync(McDevice device, int start, int count,
        CancellationToken ct)
    {
        byte[] data = await RequestAsync(CommandBatchRead, SubcommandBit, device, start, count,
            Array.Empty<byte>(), ct).ConfigureAwait(false);
        return UnpackBits(data, count);
    }

    public Task WriteBitsAsync(McDevice device, int start, IReadOnlyList<bool> values,
        CancellationToken ct)
    {
        Guard.ThrowIfNull(values, nameof(values));
        return RequestAsync(CommandBatchWrite, SubcommandBit, device, start, values.Count,
            PackBits(values), ct);
    }

    /// <summary>
    /// Khung yêu cầu 3E nhị phân. Ví dụ đọc 3 word từ D100 (tài liệu MC của Mitsubishi):
    /// <c>50 00 00 FF FF 03 00 0C 00 10 00 01 04 00 00 64 00 00 A8 03 00</c>.
    /// </summary>
    public static byte[] BuildRequest(ushort command, ushort subcommand, McDevice device, int start,
        int count, byte[] data, ushort monitoringTimer = MonitoringTimer)
    {
        Guard.ThrowIfNull(data, nameof(data));
        int length = 12 + data.Length;   // từ "monitoring timer" tới hết khung
        List<byte> frame = new()
        {
            0x50, 0x00,         // subheader yêu cầu
            0x00,               // network No.
            0xFF,               // PC No.
            0xFF, 0x03,         // module I/O đích 0x03FF (CPU)
            0x00,               // station No.
            (byte)length, (byte)(length >> 8),
            (byte)monitoringTimer, (byte)(monitoringTimer >> 8),
            (byte)command, (byte)(command >> 8),
            (byte)subcommand, (byte)(subcommand >> 8),
            (byte)start, (byte)(start >> 8), (byte)(start >> 16),   // số thiết bị: 3 byte
            (byte)device,
            (byte)count, (byte)(count >> 8),
        };
        frame.AddRange(data);
        return frame.ToArray();
    }

    /// <summary>
    /// Bit theo đơn vị bit: mỗi byte hai điểm, điểm đầu ở 4 bit cao (0x10), điểm sau ở 4 bit thấp.
    /// </summary>
    public static byte[] PackBits(IReadOnlyList<bool> values)
    {
        Guard.ThrowIfNull(values, nameof(values));
        byte[] data = new byte[(values.Count + 1) / 2];
        for (int i = 0; i < values.Count; i++)
        {
            if (values[i])
            {
                data[i / 2] |= (byte)(i % 2 == 0 ? 0x10 : 0x01);
            }
        }
        return data;
    }

    public static bool[] UnpackBits(byte[] data, int count)
    {
        Guard.ThrowIfNull(data, nameof(data));
        if (data.Length != (count + 1) / 2)
        {
            throw new McProtocolException(
                $"Expected {(count + 1) / 2} bit bytes, got {data.Length}");
        }
        bool[] bits = new bool[count];
        for (int i = 0; i < count; i++)
        {
            bits[i] = (data[i / 2] & (i % 2 == 0 ? 0x10 : 0x01)) != 0;
        }
        return bits;
    }

    public void Dispose()
    {
        Close();
        _gate.Dispose();
    }

    private void Close()
    {
        _stream?.Dispose();
        _client?.Close();
        _stream = null;
        _client = null;
    }

    private async Task<byte[]> RequestAsync(ushort command, ushort subcommand, McDevice device,
        int start, int count, byte[] data, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            NetworkStream stream = _stream ?? throw new McProtocolException("Not connected");
            byte[] request = BuildRequest(command, subcommand, device, start, count, data);
            using CancellationTokenSource timeout =
                CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(_timeout);
            // NetworkStream của net48 không dừng ReadAsync theo token: hết hạn thì ĐÓNG kết nối,
            // lời gọi đang chờ ném lỗi ngay. Khung đọc dở cũng làm lệch luồng byte — đằng nào
            // cũng phải nối lại từ đầu.
            using CancellationTokenRegistration closeOnTimeout = timeout.Token.Register(Close);
            try
            {
                await stream.WriteAsync(request, 0, request.Length).ConfigureAwait(false);
                byte[] header = await ReadExactAsync(stream, 9).ConfigureAwait(false);
                if (header[0] != 0xD0 || header[1] != 0x00)
                {
                    throw new McProtocolException(
                        $"Bad response subheader {header[0]:X2}{header[1]:X2}");
                }
                int length = header[7] | (header[8] << 8);
                byte[] body = await ReadExactAsync(stream, length).ConfigureAwait(false);
                int endCode = body[0] | (body[1] << 8);
                if (endCode != 0)
                {
                    throw new McProtocolException($"PLC end code 0x{endCode:X4}", endCode);
                }
                byte[] result = new byte[length - 2];
                Array.Copy(body, 2, result, 0, result.Length);
                return result;
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or
                                           McProtocolException or SocketException)
            {
                Close();
                ct.ThrowIfCancellationRequested();
                if (ex is McProtocolException mc && mc.EndCode != 0)
                {
                    // PLC trả lỗi đàng hoàng: kết nối không hỏng, nhưng vẫn nối lại cho sạch.
                    throw;
                }
                throw new McProtocolException(timeout.IsCancellationRequested
                    ? $"MC request to {_host}:{_port} timed out after " +
                      $"{_timeout.TotalMilliseconds:0} ms"
                    : $"MC request to {_host}:{_port} failed: {ex.Message}", ex);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private static async Task<byte[]> ReadExactAsync(NetworkStream stream, int count)
    {
        byte[] buffer = new byte[count];
        int read = 0;
        while (read < count)
        {
            int n = await stream.ReadAsync(buffer, read, count - read).ConfigureAwait(false);
            if (n == 0)
            {
                throw new IOException("PLC closed the connection");
            }
            read += n;
        }
        return buffer;
    }
}
