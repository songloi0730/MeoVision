using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MeoVision.App.Comm;
using MeoVision.App.Comm.Plc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MeoVision.App.Tests;

/// <summary>
/// Server MC 3E nhị phân giả, đủ cho đọc/ghi liên tiếp D và M — đóng vai PLC Mitsubishi trong test
/// (máy viết sách không có PLC thật).
/// </summary>
internal sealed class FakeMcServer : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _cts = new();
    private readonly object _sync = new();
    private readonly ushort[] _d = new ushort[4096];
    private readonly bool[] _m = new bool[4096];

    public FakeMcServer()
    {
        _listener.Start();
        _ = AcceptAsync();
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    /// <summary>Khác 0: mọi yêu cầu bị trả mã lỗi này (thử nhánh PLC báo lỗi).</summary>
    public int EndCode { get; set; }

    public List<byte[]> Requests { get; } = new();

    public ushort D(int i) { lock (_sync) { return _d[i]; } }

    public bool M(int i) { lock (_sync) { return _m[i]; } }

    public void SetD(int i, params ushort[] v) { lock (_sync) { Array.Copy(v, 0, _d, i, v.Length); } }

    public void SetM(int i, bool v) { lock (_sync) { _m[i] = v; } }

    public void Dispose()
    {
        _cts.Cancel();
        _listener.Stop();
    }

    private async Task AcceptAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(); }
            catch (Exception) { return; }
            _ = ServeAsync(client);
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using (client)
        {
            NetworkStream s = client.GetStream();
            try
            {
                while (true)
                {
                    byte[] head = await ReadAsync(s, 9);
                    int len = head[7] | (head[8] << 8);
                    byte[] body = await ReadAsync(s, len);
                    lock (_sync) { Requests.Add(head.Concat(body).ToArray()); }
                    byte[] reply = Handle(body);
                    await s.WriteAsync(reply, 0, reply.Length);
                }
            }
            catch (Exception) { }
        }
    }

    private byte[] Handle(byte[] b)
    {
        int cmd = b[2] | (b[3] << 8), sub = b[4] | (b[5] << 8);
        int start = b[6] | (b[7] << 8) | (b[8] << 16), code = b[9], count = b[10] | (b[11] << 8);
        List<byte> data = new();
        if (EndCode == 0)
        {
            lock (_sync)
            {
                if (cmd == 0x0401 && sub == 0 && code == 0xA8)
                {
                    for (int i = 0; i < count; i++) { data.Add((byte)_d[start + i]); data.Add((byte)(_d[start + i] >> 8)); }
                }
                else if (cmd == 0x0401 && sub == 1 && code == 0x90)
                {
                    data.AddRange(McProtocolClient.PackBits(_m.Skip(start).Take(count).ToList()));
                }
                else if (cmd == 0x1401 && sub == 0 && code == 0xA8)
                {
                    for (int i = 0; i < count; i++) { _d[start + i] = (ushort)(b[12 + 2 * i] | (b[13 + 2 * i] << 8)); }
                }
                else if (cmd == 0x1401 && sub == 1 && code == 0x90)
                {
                    bool[] bits = McProtocolClient.UnpackBits(b.Skip(12).ToArray(), count);
                    Array.Copy(bits, 0, _m, start, count);
                }
            }
        }
        int length = 2 + data.Count;
        List<byte> r = new() { 0xD0, 0x00, 0x00, 0xFF, 0xFF, 0x03, 0x00, (byte)length, (byte)(length >> 8),
            (byte)EndCode, (byte)(EndCode >> 8) };
        r.AddRange(data);
        return r.ToArray();
    }

    private static async Task<byte[]> ReadAsync(NetworkStream s, int n)
    {
        byte[] buf = new byte[n];
        int read = 0;
        while (read < n)
        {
            int k = await s.ReadAsync(buf, read, n - read);
            if (k == 0) { throw new System.IO.IOException("closed"); }
            read += k;
        }
        return buf;
    }
}

/// <summary>Kênh TCP, giao thức MC 3E và kênh PLC (Chương 21, mục 21.3–21.4).</summary>
public sealed class ChannelTests
{
    private static TcpChannelOptions Tcp(params IPAddress[] allowed) =>
        new(IPAddress.Loopback, 0, allowed, "\r", 4);

    [Fact]
    public async Task Tcp_Accepts_Cr_Lf_Or_Split_Packets_And_Answers_Each_Line()
    {
        using TcpChannel channel = new("CH1", Tcp(), (ch, line, ct) => Task.FromResult("R:" + line),
            NullLogger.Instance);
        await channel.StartAsync(CancellationToken.None);
        using TcpClient client = new();
        await client.ConnectAsync(IPAddress.Loopback, channel.Port);
        NetworkStream s = client.GetStream();

        await Write(s, "T1,P-1,SN1\r\nT3,P-1,");
        await Task.Delay(50);
        await Write(s, "SN2\n");
        string replies = await ReadAsync(s, "R:T1,P-1,SN1\rR:T3,P-1,SN2\r".Length);

        replies.Split('\r').Where(r => r.Length > 0).Should()
            .BeEquivalentTo("R:T1,P-1,SN1", "R:T3,P-1,SN2");
        channel.Connections.Should().Be(1);
    }

    [Fact]
    public async Task Tcp_Refuses_A_Client_That_Is_Not_Allowed()
    {
        using TcpChannel channel = new("CH1", Tcp(IPAddress.Parse("192.168.1.30")),
            (ch, line, ct) => Task.FromResult("R"), NullLogger.Instance);
        await channel.StartAsync(CancellationToken.None);
        using TcpClient client = new();
        await client.ConnectAsync(IPAddress.Loopback, channel.Port);

        int n = await client.GetStream().ReadAsync(new byte[16], 0, 16);

        n.Should().Be(0);   // server đóng kết nối ngay
    }

    [Fact]
    public void Mc_Read_Frame_Matches_The_Mitsubishi_Manual_Example()
    {
        byte[] frame = McProtocolClient.BuildRequest(McProtocolClient.CommandBatchRead,
            McProtocolClient.SubcommandWord, McDevice.D, 100, 3, Array.Empty<byte>(), 0x0010);

        BitConverter.ToString(frame).Should().Be(
            "50-00-00-FF-FF-03-00-0C-00-10-00-01-04-00-00-64-00-00-A8-03-00");
    }

    [Fact]
    public void Mc_Bits_Pack_Two_Per_Byte_High_Nibble_First()
    {
        byte[] packed = McProtocolClient.PackBits(new[] { true, false, false, true, true });

        packed.Should().Equal(0x10, 0x01, 0x10);
        McProtocolClient.UnpackBits(packed, 5).Should().Equal(true, false, false, true, true);
    }

    [Fact]
    public async Task Mc_Client_Reads_And_Writes_Words_And_Bits_And_Reports_Plc_Errors()
    {
        using FakeMcServer plc = new();
        using McProtocolClient mc = new("127.0.0.1", plc.Port, TimeSpan.FromSeconds(1));
        await mc.ConnectAsync(CancellationToken.None);

        await mc.WriteWordsAsync(McDevice.D, 200, new ushort[] { 1, 0xABCD }, CancellationToken.None);
        await mc.WriteBitsAsync(McDevice.M, 100, new[] { true, false, true }, CancellationToken.None);

        (await mc.ReadWordsAsync(McDevice.D, 200, 2, CancellationToken.None)).Should().Equal(1, 0xABCD);
        (await mc.ReadBitsAsync(McDevice.M, 100, 3, CancellationToken.None)).Should().Equal(true, false, true);
        plc.EndCode = 0xC051;
        Func<Task> read = () => mc.ReadWordsAsync(McDevice.D, 200, 2, CancellationToken.None);
        (await read.Should().ThrowAsync<McProtocolException>()).Which.EndCode.Should().Be(0xC051);
    }

    [Fact]
    public async Task Plc_Handshake_Trigger_Ack_Result_Complete_Then_Clears_When_Trigger_Drops()
    {
        using FakeMcServer plc = new();
        ConcurrentQueue<string> received = new();
        PlcChannelOptions options = new("127.0.0.1", plc.Port, 100, 100, TimeSpan.FromMilliseconds(10),
            TimeSpan.FromSeconds(1), 2);
        using PlcChannel channel = new("CH1", options, (ch, line, ct) =>
        {
            received.Enqueue(line);
            return Task.FromResult("T1,SN88213,OK,,1.5,-2.25");
        }, () => true, NullLogger.Instance);
        await channel.StartAsync(CancellationToken.None);

        // Phía PLC: ghi mã lệnh, sản phẩm, serial (ASCII, byte thấp trước) rồi bật Trigger (M100).
        plc.SetD(100 + PlcChannel.Word.Code, Ascii("T1", 4));
        plc.SetD(100 + PlcChannel.Word.Product, Ascii("P-1024", 8));
        plc.SetD(100 + PlcChannel.Word.Serial, Ascii("SN88213", 10));
        plc.SetM(100 + PlcChannel.Bit.Trigger, true);
        await Until(() => plc.M(100 + PlcChannel.Bit.InspectionComplete));

        received.Should().Equal("T1,P-1024,SN88213");
        plc.M(100 + PlcChannel.Bit.TriggerAck).Should().BeTrue();
        plc.M(100 + PlcChannel.Bit.ResultsValid).Should().BeTrue();
        plc.M(100 + PlcChannel.Bit.Error).Should().BeFalse();
        plc.D(100 + PlcChannel.Word.Status).Should().Be(1);
        Real(plc, 100 + PlcChannel.Word.Outputs).Should().Be(1.5f);
        Real(plc, 100 + PlcChannel.Word.Outputs + 2).Should().Be(-2.25f);

        plc.SetM(100 + PlcChannel.Bit.Trigger, false);
        await Until(() => !plc.M(100 + PlcChannel.Bit.TriggerAck) &&
                          !plc.M(100 + PlcChannel.Bit.InspectionComplete));
        channel.Connections.Should().Be(1);
    }

    [Fact]
    public async Task Plc_Bypass_Is_Neither_Valid_Nor_Error_And_Has_Its_Own_Bit_And_Status()
    {
        // DESIGN_LOG [28] D1: máy phải phân biệt "không kiểm" với OK và với LỖI.
        using FakeMcServer plc = new();
        PlcChannelOptions options = new("127.0.0.1", plc.Port, 100, 100, TimeSpan.FromMilliseconds(10),
            TimeSpan.FromSeconds(1), 2);
        using PlcChannel channel = new("CH1", options,
            (ch, line, ct) => Task.FromResult("T1,SN1,BYP,BYPASS,,"), () => true, NullLogger.Instance);
        await channel.StartAsync(CancellationToken.None);
        plc.SetD(100 + PlcChannel.Word.Code, Ascii("T1", 4));
        plc.SetM(100 + PlcChannel.Bit.Trigger, true);
        await Until(() => plc.M(100 + PlcChannel.Bit.InspectionComplete));

        plc.D(100 + PlcChannel.Word.Status).Should().Be(4);
        plc.M(100 + PlcChannel.Bit.Bypass).Should().BeTrue();
        plc.M(100 + PlcChannel.Bit.ResultsValid).Should().BeFalse();
        plc.M(100 + PlcChannel.Bit.Error).Should().BeFalse();

        plc.SetM(100 + PlcChannel.Bit.Trigger, false);
        await Until(() => !plc.M(100 + PlcChannel.Bit.Bypass));
    }

    private static ushort[] Ascii(string text, int words)
    {
        byte[] b = Encoding.ASCII.GetBytes(text.PadRight(words * 2, '\0'));
        return Enumerable.Range(0, words).Select(i => (ushort)(b[2 * i] | (b[2 * i + 1] << 8))).ToArray();
    }

    private static float Real(FakeMcServer plc, int at)
    {
        ushort lo = plc.D(at), hi = plc.D(at + 1);
        return BitConverter.ToSingle(new[] { (byte)lo, (byte)(lo >> 8), (byte)hi, (byte)(hi >> 8) }, 0);
    }

    private static async Task Until(Func<bool> condition)
    {
        for (int i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(10);
        }
        condition().Should().BeTrue();
    }

    private static Task Write(NetworkStream s, string text)
    {
        byte[] b = Encoding.ASCII.GetBytes(text);
        return s.WriteAsync(b, 0, b.Length);
    }

    private static async Task<string> ReadAsync(NetworkStream s, int length)
    {
        byte[] buf = new byte[length];
        int read = 0;
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(3));
        while (read < length)
        {
            int n = await s.ReadAsync(buf, read, length - read, cts.Token);
            if (n == 0) { break; }
            read += n;
        }
        return Encoding.ASCII.GetString(buf, 0, read);
    }
}
