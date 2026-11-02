using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Cognex.VisionPro;
using Cognex.VisionPro.ImageFile;
using Cognex.VisionPro.Implementation;
using FluentAssertions;
using MeoVision.Core.Devices;
using MeoVision.Vision.Devices;
using MeoVision.Vision.Storage;
using Xunit;
using Xunit.Abstractions;

namespace MeoVision.Vision.Tests;

/// <summary>
/// Chương 23 chạy THẬT trên VisionPro: ảnh lưu ra BMP đọc lại đúng từng pixel, ảnh đọc lại vẫn dùng
/// được sau khi file đã đóng, và ghi một ảnh 5 MP mất bao lâu (để đặt giới hạn hàng đợi ảnh).
/// </summary>
public sealed class ImageStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mv-img-" +
        Guid.NewGuid().ToString("N"));
    private readonly ITestOutputHelper _output;

    public ImageStoreTests(ITestOutputHelper output)
    {
        _output = output;
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void Saved_Image_Reads_Back_Pixel_For_Pixel_And_Outlives_The_File()
    {
        CogImageStore store = new();
        using CameraImage original = Blobs();
        string file = Path.Combine(_root, "2026-10-28", "T3", "093001_123_SN-1_NG.bmp");

        store.Save(original, file);
        using CameraImage loaded = store.Load(file);

        Directory.GetFiles(Path.GetDirectoryName(file)!).Should().Equal(file);   // không còn file tạm
        loaded.Width.Should().Be(original.Width);
        loaded.Height.Should().Be(original.Height);
        Pixels(loaded).Should().Equal(Pixels(original));   // đọc SAU khi CogImageFile đã đóng
    }

    [Fact]
    public void Writing_A_5_Megapixel_Image_Takes_Milliseconds_Not_Seconds()
    {
        CogImageStore store = new();
        using CameraImage big = new CogCameraImage(new CogImage8Grey(2448, 2048), "5MP",
            DateTime.Now);
        double[] times = Enumerable.Range(0, 10).Select(i =>
        {
            Stopwatch watch = Stopwatch.StartNew();
            store.Save(big, Path.Combine(_root, "big" + i + ".bmp"));
            return watch.Elapsed.TotalMilliseconds;
        }).OrderBy(t => t).ToArray();

        _output.WriteLine($"5 MP BMP: median {times[5]:0.0} ms, max {times[9]:0.0} ms, " +
                          $"{new FileInfo(Path.Combine(_root, "big0.bmp")).Length / 1e6:0.0} MB");
        times[5].Should().BeLessThan(500);
    }

    [Fact]
    public void Annotated_Copy_Has_The_Result_Graphics_Burned_Into_The_Image()
    {
        using CameraImage blobs = Blobs();
        ICogImage image = CogCameraImage.From(blobs);
        CogRectangle box = new() { Color = CogColorConstants.Red, LineWidthInScreenPixels = 6 };
        box.SetCenterWidthHeight(image.Width / 2.0, image.Height / 2.0, image.Width / 2.0,
            image.Height / 2.0);
        CogRecord top = new("LastRun", typeof(ICogImage), CogRecordUsageConstants.Result, false,
            null, "LastRun");
        CogRecord view = new("LastRun.OutputImage", typeof(ICogImage),
            CogRecordUsageConstants.Result, false, image, "OutputImage");
        view.SubRecords.Add(new CogRecord("Box", typeof(CogRectangle),
            CogRecordUsageConstants.Result, false, box, "Box"));
        top.SubRecords.Add(view);
        string file = Path.Combine(_root, "2026-10-30", "T1", "093001_123_SN-1_NG_view.jpg");
        using CogImageStore store = new();

        bool saved = store.SaveAnnotated(top, string.Empty, file);

        saved.Should().BeTrue();
        Directory.GetFiles(Path.GetDirectoryName(file)!).Should().Equal(file);   // không còn file tạm
        using System.Drawing.Bitmap bitmap = new(file);
        bitmap.Width.Should().Be(image.Width);
        int red = 0;
        for (int y = 0; y < bitmap.Height; y += 2)
        {
            for (int x = 0; x < bitmap.Width; x += 2)
            {
                System.Drawing.Color c = bitmap.GetPixel(x, y);
                red += c.R > 180 && c.G < 90 && c.B < 90 ? 1 : 0;
            }
        }
        _output.WriteLine($"red pixels (1/4 sampled): {red}");
        red.Should().BeGreaterThan(50);   // ảnh xám gốc không có điểm đỏ nào
        store.SaveAnnotated(new CogRecord(), string.Empty, file + "2").Should().BeFalse();
    }

    private static CameraImage Blobs()
    {
        using CogImageFile file = new();
        file.Open(@"C:\Program Files\Cognex\VisionPro\Images\blobs.bmp", CogImageFileModeConstants.Read);
        return new CogCameraImage(file[0], "blobs.bmp", DateTime.Now);
    }

    private static byte[] Pixels(CameraImage image)
    {
        CogImage8Grey grey = (CogImage8Grey)CogCameraImage.From(image);
        ICogImage8PixelMemory memory = grey.Get8GreyPixelMemory(CogImageDataModeConstants.Read, 0, 0,
            grey.Width, grey.Height);
        try
        {
            byte[] all = new byte[grey.Width * grey.Height];
            for (int y = 0; y < grey.Height; y++)
            {
                System.Runtime.InteropServices.Marshal.Copy(memory.Scan0 + y * memory.Stride, all,
                    y * grey.Width, grey.Width);
            }
            return all;
        }
        finally
        {
            memory.Dispose();
        }
    }
}
