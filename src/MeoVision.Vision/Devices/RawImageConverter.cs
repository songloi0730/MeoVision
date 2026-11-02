using System;
using System.Runtime.InteropServices;
using Cognex.VisionPro;

namespace MeoVision.Vision.Devices;

/// <summary>
/// Chép buffer ảnh xám 8-bit của SDK camera hãng khác (Basler pylon, Hikrobot MVS...) thành
/// <see cref="CogImage8Grey"/> cho job VisionPro (Chương 18, mục 18.7). Chép là bắt buộc: buffer
/// thuộc về SDK hãng và sẽ bị dùng lại cho khung hình kế tiếp ngay khi trả về.
/// </summary>
public static class RawImageConverter
{
    /// <param name="buffer">Địa chỉ pixel (0,0) của buffer SDK.</param>
    /// <param name="stride">Số byte một dòng trong buffer — có thể lớn hơn width (đệm dòng).</param>
    public static CogImage8Grey FromGray8(IntPtr buffer, int width, int height, int stride)
    {
        if (buffer == IntPtr.Zero)
        {
            throw new ArgumentException("Null buffer", nameof(buffer));
        }
        if (width <= 0 || height <= 0 || stride < width)
        {
            throw new ArgumentException($"Bad size {width}x{height}, stride {stride}");
        }

        CogImage8Grey image = new(width, height);
        ICogImage8PixelMemory memory = image.Get8GreyPixelMemory(
            CogImageDataModeConstants.Write, 0, 0, width, height);
        try
        {
            // Chép TỪNG DÒNG: stride của buffer nguồn và của ảnh Cognex thường khác nhau.
            byte[] row = new byte[width];
            for (int y = 0; y < height; y++)
            {
                Marshal.Copy(buffer + y * stride, row, 0, width);
                Marshal.Copy(row, 0, memory.Scan0 + y * memory.Stride, width);
            }
        }
        finally
        {
            // ICogImage8PixelMemory có Dispose() nhưng KHÔNG kế thừa IDisposable (không dùng
            // được using). Theo tài liệu Cognex, pixel ghi vào có thể chưa nằm trong ảnh cho tới
            // khi gọi Dispose — quên gọi là ảnh trả về toàn số 0.
            memory.Dispose();
        }
        return image;
    }

    /// <summary>Phiên bản cho buffer đã ở dạng mảng byte (SDK trả về byte[]).</summary>
    public static CogImage8Grey FromGray8(byte[] pixels, int width, int height, int stride)
    {
        if (pixels is null || pixels.Length < (long)stride * height)
        {
            throw new ArgumentException("Buffer smaller than stride x height", nameof(pixels));
        }
        GCHandle pin = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            return FromGray8(pin.AddrOfPinnedObject(), width, height, stride);
        }
        finally
        {
            pin.Free();
        }
    }
}
