using System;
using System.Threading;
using System.Threading.Tasks;

namespace MeoVision.App.Comm;

/// <summary>
/// Xử lý một dòng nhận được trên một kênh, trả đúng một dòng trả lời (CommandService).
/// </summary>
public delegate Task<string> LineHandler(string channel, string line, CancellationToken ct);

/// <summary>
/// Một kênh nhận lệnh (Chương 19, mục 19.2; Chương 21): một cổng TCP hoặc một vùng bắt tay PLC.
/// Kênh chỉ lo đường truyền; nội dung bản tin là việc của <see cref="CommandService"/>.
/// </summary>
public interface IChannel : IDisposable
{
    string Id { get; }

    /// <summary>Mô tả để ghi log / hiện ở thanh trạng thái, ví dụ "TCP 127.0.0.1:10001".</summary>
    string Description { get; }

    /// <summary>Số kết nối đang mở (client TCP; với PLC: 1 khi đã nối).</summary>
    int Connections { get; }

    /// <summary>Lúc nhận bản tin gần nhất — nhịp tim của máy nhìn từ phía vision.</summary>
    DateTime? LastMessageUtc { get; }

    event EventHandler? StatusChanged;

    Task StartAsync(CancellationToken ct);

    void Stop();
}
