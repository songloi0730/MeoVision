using System;
using Cognex.VisionPro;
using MeoVision.Core;
using MeoVision.Core.Devices;

namespace MeoVision.Vision.Devices;

/// <summary>
/// Ảnh camera mang ảnh Cognex thật bên trong (Chương 18, mục 18.3). Chỉ code trong
/// MeoVision.Vision mới lấy <see cref="Image"/> ra — job ở Chương 20 làm việc đó.
/// </summary>
public sealed class CogCameraImage : CameraImage
{
    public CogCameraImage(ICogImage image, string source, DateTime capturedAt)
        : base(source, Guard.NotNull(image, nameof(image)).Width, image.Height, capturedAt)
    {
        Image = image;
    }

    public ICogImage Image { get; }

    /// <summary>
    /// Ảnh Cognex bên trong một CameraImage — ném lỗi rõ ràng nếu đó không phải ảnh Cognex.
    /// </summary>
    public static ICogImage From(CameraImage image) =>
        (image as CogCameraImage)?.Image ??
        throw new ArgumentException("Not a Cognex camera image: " + image?.GetType().Name,
            nameof(image));

    /// <summary>
    /// Bản sao có cây hệ toạ độ RIÊNG, giữ nguyên các hệ đã có (hiệu chuẩn, Fixture của job trước).
    /// Đo thật (MeoVision DESIGN_LOG [31]): Fixture của một job khác ghi đè hệ "Fixture" mà job
    /// trước đã tạo trên cùng ảnh → lỗi "modified a coordinate space that it did not create";
    /// <c>Copy()</c> và <c>CopyBase()</c> vẫn DÙNG CHUNG cây đó, chỉ constructor sao chép tách ra.
    /// </summary>
    public override CameraImage? CopyForAnotherJob()
    {
        ICogImage copy = Image switch
        {
            CogImage8Grey grey => new CogImage8Grey(grey),
            CogImage16Grey grey16 => new CogImage16Grey(grey16),
            CogImage24PlanarColor color => new CogImage24PlanarColor(color),
            _ => throw new NotSupportedException("Cannot copy image type " + Image.GetType().Name),
        };
        return new CogCameraImage(copy, Source, CapturedAt);
    }

    protected override void Dispose(bool disposing)
    {
        // ICogImage không khai báo IDisposable, nhưng lớp cụ thể (CogImage8Grey...) có —
        // trả bộ nhớ ảnh ngay, không chờ GC (Chương 13, mục 13.6.1).
        if (disposing)
        {
            (Image as IDisposable)?.Dispose();
        }
    }
}
