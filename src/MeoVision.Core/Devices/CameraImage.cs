using System;

namespace MeoVision.Core.Devices;

/// <summary>
/// Ảnh vừa chụp, dạng "tay cầm" không mở ra được ở tầng App (Chương 18, mục 18.3). Lớp cài đặt
/// trong MeoVision.Vision giữ ảnh Cognex thật bên trong; job (Chương 20) lấy lại nó. Nhờ vậy App
/// chuyển ảnh từ camera tới job, đo thời gian, xử lý timeout mà không biết Cognex.
/// </summary>
public abstract class CameraImage : IDisposable
{
    protected CameraImage(string source, int width, int height, DateTime capturedAt)
    {
        Source = source;
        Width = width;
        Height = height;
        CapturedAt = capturedAt;
    }

    /// <summary>Nguồn ảnh: mô tả camera, hoặc tên file khi đọc từ thư mục.</summary>
    public string Source { get; }

    public int Width { get; }

    public int Height { get; }

    public DateTime CapturedAt { get; }

    /// <summary>
    /// Bản sao để một job KHÁC chạy trên cùng ảnh mà không đụng hệ toạ độ job trước đã gắn vào.
    /// Null = ảnh không mang hệ toạ độ (ảnh giả trong test) — dùng chung được. Người gọi sở hữu
    /// bản sao và phải Dispose nó.
    /// </summary>
    public virtual CameraImage? CopyForAnotherJob() => null;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
    }
}
