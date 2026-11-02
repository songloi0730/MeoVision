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
