using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cognex.VisionPro;
using FluentAssertions;
using MeoVision.Core.Devices;
using MeoVision.Vision.Devices;
using Xunit;
using Xunit.Abstractions;

namespace MeoVision.Vision.Tests;

/// <summary>Chạy THẬT trên VisionPro cài trong máy, dùng ảnh mẫu đi kèm bộ cài.</summary>
public sealed class DeviceIntegrationTests : IDisposable
{
    private static readonly string Samples = @"C:\Program Files\Cognex\VisionPro\Images";
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "mv-folder-" +
        Guid.NewGuid().ToString("N"));
    private readonly ITestOutputHelper _output;

    public DeviceIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
        Directory.CreateDirectory(_folder);
        // Đặt tên để cố định thứ tự: a = bmp 1 ảnh, b = idb 6 ảnh, c = tif màu 17 ảnh.
        File.Copy(Path.Combine(Samples, "100flat.bmp"), Path.Combine(_folder, "a.bmp"));
        File.Copy(Path.Combine(Samples, "CheckCal.idb"), Path.Combine(_folder, "b.idb"));
        File.Copy(Path.Combine(Samples, "JellOBoxes.tif"), Path.Combine(_folder, "c.tif"));
        File.WriteAllText(Path.Combine(_folder, "notes.txt"), "not an image");
        // Ảnh mẫu trong thư mục cài đặt là chỉ-đọc, bản chép mang theo thuộc tính đó.
        foreach (string file in Directory.GetFiles(_folder))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }
    }

    [Fact]
    public async Task Folder_Camera_Returns_Every_Image_Inside_Every_File_In_Name_Order()
    {
        using FolderCamera camera = Create(loop: false);
        await camera.OpenAsync(CancellationToken.None);

        List<string> sources = new();
        List<CameraImage> images = new();
        for (int i = 0; i < 1 + 6 + 17; i++)
        {
            CameraImage image = await camera.GrabAsync(CaptureSettings.Default,
                CancellationToken.None);
            sources.Add(image.Source);
            images.Add(image);
        }

        sources.First().Should().Be("a.bmp");
        sources.Skip(1).Take(6).Should().Equal(Enumerable.Range(0, 6).Select(i => "b.idb#" + i));
        sources.Last().Should().Be("c.tif#16");
        // Ảnh màu của .tif đã được đổi sang xám vì grey = true.
        images.Select(CogCameraImage.From).Should().AllBeOfType<CogImage8Grey>();
        // Ảnh vẫn dùng được sau khi file chứa nó đã đóng (camera đã chuyển sang file sau).
        CogCameraImage.From(images[0]).Width.Should().Be(480);

        Func<Task> pastEnd = () => camera.GrabAsync(CaptureSettings.Default,
            CancellationToken.None);
        (await pastEnd.Should().ThrowAsync<DeviceException>()).Which.Info.Key
            .Should().Be("camera.folder.end");
        images.ForEach(i => i.Dispose());
    }

    [Fact]
    public async Task Folder_Camera_Loops_Back_To_The_First_Image()
    {
        using FolderCamera camera = Create(loop: true, patterns: "*.bmp");
        await camera.OpenAsync(CancellationToken.None);

        using CameraImage first = await camera.GrabAsync(CaptureSettings.Default, default);
        using CameraImage again = await camera.GrabAsync(CaptureSettings.Default, default);

        again.Source.Should().Be(first.Source);
    }

    [Fact]
    public async Task Folder_Camera_Fails_Clearly_When_Folder_Is_Missing_Or_Has_No_Image()
    {
        using FolderCamera missing = new("TopCam", Path.Combine(_folder, "nope"),
            new[] { "*.bmp" }, true, true);
        Func<Task> open = () => missing.OpenAsync(CancellationToken.None);
        (await open.Should().ThrowAsync<DeviceException>()).Which.Info.Key
            .Should().Be("camera.folder.notFound");

        using FolderCamera empty = Create(loop: true, patterns: "*.png");
        open = () => empty.OpenAsync(CancellationToken.None);
        (await open.Should().ThrowAsync<DeviceException>()).Which.Info.Key
            .Should().Be("camera.folder.empty");
    }

    [Fact]
    public void Raw_Buffer_With_Row_Padding_Is_Copied_Pixel_Exact()
    {
        const int width = 2448, height = 2048, stride = 2464;   // SDK đệm mỗi dòng 16 byte
        byte[] buffer = new byte[stride * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                buffer[y * stride + x] = (byte)((x + y) % 251);
            }
            buffer[y * stride + width] = 0xEE;   // byte đệm — không được lọt vào ảnh
        }

        Stopwatch sw = Stopwatch.StartNew();
        CogImage8Grey image = RawImageConverter.FromGray8(buffer, width, height, stride);
        sw.Stop();
        _output.WriteLine($"{width}x{height} converted in {sw.ElapsedMilliseconds} ms");

        image.Width.Should().Be(width);
        image.GetPixel(0, 0).Should().Be(0);
        image.GetPixel(width - 1, 0).Should().Be((byte)((width - 1) % 251));
        image.GetPixel(100, 2000).Should().Be((byte)(2100 % 251));
        image.GetPixel(width - 1, height - 1).Should().Be(
            (byte)((width - 1 + height - 1) % 251));
        ((IDisposable)image).Dispose();   // CogImage8Grey cài IDisposable tường minh
    }

    [Fact]
    public async Task GigE_Camera_With_Unknown_Serial_Lists_What_Was_Found()
    {
        using CognexGigECamera camera = new("TopCam", "NO-SUCH-SERIAL", string.Empty, 2000);

        Func<Task> open = () => camera.OpenAsync(CancellationToken.None);

        DeviceException ex = (await open.Should().ThrowAsync<DeviceException>()).Which;
        ex.Info.Key.Should().Be("camera.gige.notFound");
        _output.WriteLine("Found on this machine: " + ex.Info.Args[2]);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            // file có thể còn bị giữ trong lúc test lỗi — thư mục tạm, bỏ qua
        }
    }

    private FolderCamera Create(bool loop, string patterns = "*.bmp;*.idb;*.tif;*.png") =>
        new("TopCam", _folder, patterns.Split(';'), loop, toGrey: true);
}

public class MachineExampleTests
{
    [Fact]
    public void Shipped_Machine_Example_Is_Valid_With_The_Real_Factories()
    {
        MeoVision.App.Devices.MachineConfigLoader loader = new(new IDeviceFactory[]
        {
            new CognexGigECameraFactory(), new FolderCameraFactory(),
            new MeoVision.App.Devices.Lights.AsciiLightFactory(),
        });

        MeoVision.App.Devices.MachineConfigResult result = loader.Load(
            Path.Combine(AppContext.BaseDirectory, "machine.example.json"));

        result.Problems.Should().BeEmpty();
        result.Config!.Cameras.Select(c => c.Role).Should().Equal("TopCam", "BottomCam");
    }

    [Fact]
    public void Shipped_Plc_Machine_Example_Is_Valid_And_A_Wrong_Plc_Address_Is_Reported()
    {
        MeoVision.App.Devices.MachineConfigLoader loader = new(new IDeviceFactory[]
        {
            new CognexGigECameraFactory(), new FolderCameraFactory(),
            new MeoVision.App.Devices.Lights.AsciiLightFactory(),
        });
        string file = Path.Combine(AppContext.BaseDirectory, "machine.plc.example.json");
        string broken = Path.Combine(Path.GetTempPath(), "mv-plc-" + Guid.NewGuid().ToString("N") + ".json");
        // bitStart phải là M, wordStart phải là D; cổng ngoài khoảng.
        File.WriteAllText(broken, File.ReadAllText(file).Replace("\"M100\"", "\"D100\"")
            .Replace("\"5002\"", "\"70000\""));

        loader.Load(file).Problems.Should().BeEmpty();
        loader.Load(broken).Problems.Select(p => p.Key).Should().Contain(
            new[] { "machine.setting.badValue", "machine.setting.outOfRange" });
        File.Delete(broken);
    }
}

public class FolderCameraFactoryTests
{
    [Fact]
    public void Single_Backslash_In_Json_Path_Is_Reported_Not_Thrown()
    {
        // "D:\images\bottom" viết trong JSON với MỘT dấu gạch chéo: "\b" thành backspace.
        DeviceConfig config = new()
        {
            Role = "BottomCam",
            Type = "Folder",
            // Chuỗi C# dưới đây chứa đúng thứ Json.NET đọc ra: "\\" là một dấu gạch chéo,
            // "\b" là ký tự backspace.
            Settings = new Dictionary<string, string> { ["folder"] = "D:\\images\bottom" },
        };

        IReadOnlyList<MeoVision.Core.Localization.Msg> problems =
            new FolderCameraFactory().Validate(config);

        problems.Single().Key.Should().Be("machine.setting.badPath");
    }
}
