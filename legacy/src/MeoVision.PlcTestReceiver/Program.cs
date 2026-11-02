// -------------------------------------------------------
// File:    Program.cs
// Project: MeoVision.PlcTestReceiver
// Purpose: Công cụ thủ công đóng vai PLC/gateway để tác giả tự kiểm tra tín hiệu
//          Trigger/Busy/Done/Ack mà MeoVision.WorkStation.Demo gửi qua TCP 127.0.0.1.
// -------------------------------------------------------
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace MeoVision.PlcTestReceiver;

// Cố tình KHÔNG reference bất kỳ project MeoVision nào khác (kể cả MeoVision.Core) — công cụ
// kiểm tra phải độc lập hoàn toàn với code đang được kiểm tra, nếu không một lỗi chung trong
// Core có thể khiến cả 2 phía "tự kiểm tra lẫn nhau" mà không phát hiện ra gì (xem
// docs/design-notes/DESIGN_LOG.md mục [6]). Vì vậy tên tín hiệu (TRIGGER/BUSY/DONE/ACK) được
// lặp lại làm hằng chuỗi ở đây thay vì chia sẻ với MeoVision.WorkStation.Demo.TcpPlcCommunication.
public static class Program
{
    // Đọc stdin bằng UTF-8 + BẬT dò BOM: nếu phía gửi lệnh (script tự động hoá, hoặc .NET
    // StreamWriter mặc định của một số host) chèn BOM UTF-8 (EF BB BF) trước dữ liệu thật — quan
    // sát thật khi kiểm thử tự động hoá qua PowerShell Process.StandardInput — reader này tự nhận
    // diện và bỏ qua đúng 3 byte đó, không làm hỏng ký tự đầu. Người gõ trực tiếp ở console thật
    // không tạo BOM nên không ảnh hưởng gì đến cách dùng thông thường.
    private static readonly StreamReader ConsoleReader =
        new(Console.OpenStandardInput(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

    public static async Task Main(string[] args)
    {
        SetConsoleOutputEncoding();

        const string host = "127.0.0.1";
        int port = 5050;
        if (args.Length > 0 && int.TryParse(args[0], out int argPort))
        {
            port = argPort;
        }

        PrintBanner(host, port);

        TcpListener listener = new(IPAddress.Parse(host), port);
        listener.Start();

        try
        {
            while (true)
            {
                Console.WriteLine($"[Hệ thống] Đang chờ kết nối tại {host}:{port}...");
                using TcpClient client = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
                Console.WriteLine($"[Hệ thống] Đã kết nối từ {client.Client.RemoteEndPoint}");
                Console.WriteLine();

                using NetworkStream stream = client.GetStream();
                using StreamReader reader = new(stream, Encoding.ASCII);
                using StreamWriter writer = new(stream, Encoding.ASCII) { AutoFlush = true, NewLine = "\n" };

                Task readLoopTask = RunReadLoopAsync(reader);

                while (!readLoopTask.IsCompleted)
                {
                    string? command = ConsoleReader.ReadLine();
                    if (string.IsNullOrEmpty(command))
                    {
                        continue;
                    }

                    await HandleCommandAsync(command.Trim(), writer, readLoopTask).ConfigureAwait(false);
                }

                Console.WriteLine();
            }
        }
        finally
        {
            listener.Stop();
        }
    }

    // Chữ tiếng Việt cần UTF-8 để hiện đúng thay vì "?" — có thể ném IOException khi không có
    // console handle thật (VD: stdout bị redirect hoàn toàn) — bỏ qua khi đó, không ảnh hưởng
    // chức năng chính.
    private static void SetConsoleOutputEncoding()
    {
        try
        {
            Console.OutputEncoding = Encoding.UTF8;
        }
        catch (IOException)
        {
        }
    }

    private static void PrintBanner(string host, int port)
    {
        Console.WriteLine("MeoVision.PlcTestReceiver — công cụ thủ công tự kiểm tra tín hiệu PLC ↔ Vision");
        Console.WriteLine($"Đóng vai PLC/gateway: lắng nghe tại {host}:{port}.");
        Console.WriteLine("MeoVision.WorkStation.Demo (appsettings PlcCommunication:Transport=Tcp) sẽ kết nối tới đây.");
        Console.WriteLine("Lệnh gõ + Enter khi đã kết nối: t = gửi TRIGGER | a = gửi ACK | q = thoát chương trình");
        Console.WriteLine();
    }

    private static async Task RunReadLoopAsync(StreamReader reader)
    {
        while (true)
        {
            string? line;
            try
            {
                line = await reader.ReadLineAsync().ConfigureAwait(false);
            }
            catch (IOException)
            {
                Console.WriteLine("[Hệ thống] Mất kết nối (lỗi đọc).");
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            if (line is null)
            {
                Console.WriteLine("[Hệ thống] Phía bên kia đã đóng kết nối.");
                return;
            }

            Console.WriteLine($"[Nhận {DateTime.Now:HH:mm:ss.fff}] {line}");
        }
    }

    private static async Task HandleCommandAsync(string command, StreamWriter writer, Task readLoopTask)
    {
        if (string.Equals(command, "q", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(command, "quit", StringComparison.OrdinalIgnoreCase))
        {
            Environment.Exit(0);
            return;
        }

        string? message = command.ToLowerInvariant() switch
        {
            "t" or "trigger" => "TRIGGER",
            "a" or "ack" => "ACK",
            _ => null
        };

        if (message is null)
        {
            Console.WriteLine("Lệnh không hợp lệ. Dùng: t (TRIGGER) | a (ACK) | q (thoát).");
            return;
        }

        if (readLoopTask.IsCompleted)
        {
            Console.WriteLine("Đã mất kết nối — không gửi được.");
            return;
        }

        try
        {
            await writer.WriteLineAsync(message).ConfigureAwait(false);
            Console.WriteLine($"[Gửi  {DateTime.Now:HH:mm:ss.fff}] {message}");
        }
        catch (IOException ex)
        {
            Console.WriteLine($"Gửi thất bại: {ex.Message}");
        }
    }
}
