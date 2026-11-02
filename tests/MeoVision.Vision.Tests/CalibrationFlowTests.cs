using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Cognex.VisionPro;
using Cognex.VisionPro.Blob;
using Cognex.VisionPro.ImageFile;
using FluentAssertions;
using MeoVision.App.Calibration;
using MeoVision.App.Comm;
using MeoVision.App.Devices;
using MeoVision.App.Hosting;
using MeoVision.App.Products;
using MeoVision.Core.Commands;
using MeoVision.Core.Devices;
using MeoVision.Vision.Commands;
using MeoVision.Vision.Devices;
using MeoVision.Vision.Products;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace MeoVision.Vision.Tests;

/// <summary>
/// Hiệu chuẩn do máy dẫn chạy THẬT trên VisionPro (Chương 21, mục 21.5): 9 ảnh có một đốm ở 9 vị trí
/// đóng vai "dấu chạy theo trục", toạ độ máy = 0,05 mm/pixel, gốc (10; 20).
/// </summary>
public sealed class CalibrationFlowTests : IDisposable
{
    private static readonly int[] Xs = { 160, 320, 480 };
    private static readonly int[] Ys = { 120, 240, 360 };
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mv-calflow-" +
        Guid.NewGuid().ToString("N"));
    private readonly ITestOutputHelper _output;
    private readonly AppPaths _paths;
    private readonly DeviceManager _devices = new(new DeviceManagerOptions(),
        NullLogger<DeviceManager>.Instance);
    private readonly CalibrationService _service;

    public CalibrationFlowTests(ITestOutputHelper output)
    {
        _output = output;
        _paths = AppPaths.Resolve(Path.Combine(_root, "data"), Path.GetTempPath());
        _paths.EnsureCreated();
        string images = Path.Combine(_root, "images");
        Directory.CreateDirectory(images);
        int k = 0;
        foreach (int y in Ys)
        {
            foreach (int x in Xs)
            {
                SaveSpot(Path.Combine(images, $"p{++k}.bmp"), x, y);
            }
        }
        _devices.Add(new FolderCamera("TopCam", images, new[] { "*.bmp" }, loop: true, toGrey: true));
        _devices.OpenAllAsync().GetAwaiter().GetResult();
        ProductStore store = new(_paths, new ProductLoader(new CogJobInspector(),
                new CogCalibrationLoader(_paths.MachineCalibration)),
            new ProductBackup(_paths), NullLogger<ProductStore>.Instance);
        _service = new CalibrationService(_paths, _devices, new CogJobInspector(),
            new CogNPointCalibrationBuilder(), store,
            () => new MachineView(new[] { "TopCam" }, new Dictionary<string, (int, int)>(),
                Array.Empty<string>(), _ => true),
            NullLogger<CalibrationService>.Instance);
    }

    public void Dispose()
    {
        _service.Dispose();
        _devices.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task Nine_Machine_Led_Points_Give_A_Calibration_The_Command_Engine_Can_Load()
    {
        WriteConfig("Cal-1", minPoints: 9);

        (await Send("CS,Cal-1")).Ok.Should().BeTrue();
        int i = 0;
        foreach (int y in Ys)
        {
            foreach (int x in Xs)
            {
                CalibrationReply point = await Send(string.Format(CultureInfo.InvariantCulture,
                    "CP,Cal-1,{0},{1:0.000},{2:0.000}", ++i, 10 + 0.05 * x, 20 + 0.05 * y));
                point.Ok.Should().BeTrue(point.Reason);
                _output.WriteLine($"CP {i}: pixel {point.Values[1]}, {point.Values[2]}");
            }
        }
        CalibrationReply end = await Send("CE,Cal-1");

        end.Ok.Should().BeTrue(end.Reason);
        _output.WriteLine($"CE: RMS {end.Values[0]} px, {end.Values[1]} points");
        double.Parse(end.Values[0], CultureInfo.InvariantCulture).Should().BeLessThan(0.05);
        File.ReadAllLines(Path.Combine(_paths.MachineCalibration, "Cal-1", "points.csv"))
            .Should().HaveCount(10);

        // Chính file đó, nạp bằng bộ nạp của Chương 20, đổi pixel ra mm đúng 0,05 mm/pixel.
        CalibrationLoad load = new CogCalibrationLoader(_paths.MachineCalibration).Load("Cal-1");
        load.Error.Should().BeNull();
        using ICalibration calibration = load.Calibration!;
        string middle = Path.Combine(_root, "images", "p5.bmp");
        using CameraImage image = Load(middle);
        calibration.Apply(image);
        (double mmX, double mmY) = LargestDark(image);
        mmX.Should().BeApproximately(10 + 0.05 * 320, 0.05);
        mmY.Should().BeApproximately(20 + 0.05 * 240, 0.05);
    }

    [Fact]
    public async Task Too_Few_Points_Or_A_Bad_Point_Never_Overwrite_The_Calibration()
    {
        WriteConfig("Cal-2", minPoints: 4);
        string target = Path.Combine(_paths.MachineCalibration, "Cal-2", CalibrationService.ResultFile);

        await Send("CS,Cal-2");
        (await Send("CE,Cal-2")).Reason.Should().Be("TOO_FEW_POINTS");   // chưa có điểm nào

        // Điểm 2 gửi sai toạ độ máy 1 mm (~20 pixel): RMS vượt ngưỡng, không ghi file. (Nếu điểm sai
        // là điểm 4 — điểm duy nhất ngoài hàng — phép biến đổi có "xiên" nuốt trọn sai số, RMS = 0:
        // ít điểm thì RMS không thấy điểm sai.)
        await Send("CS,Cal-2");
        string[] points = { "1,18.000,26.000", "2,27.000,26.000", "3,34.000,26.000", "4,18.000,32.000" };
        foreach (string p in points)
        {
            (await Send("CP,Cal-2," + p)).Ok.Should().BeTrue();
        }
        CalibrationReply end = await Send("CE,Cal-2");
        _output.WriteLine($"CE: {end.Reason} RMS {string.Join(" ", end.Values)}");

        end.Reason.Should().Be("RMS");
        File.Exists(target).Should().BeFalse();
        File.Exists(Path.Combine(_paths.MachineCalibration, "Cal-2", "points.csv")).Should().BeTrue();
    }

    [Fact]
    public async Task Four_Points_With_One_Off_The_Line_Hide_A_Wrong_Point_Behind_A_Perfect_Rms()
    {
        // Bẫy của "ít điểm": ba điểm thẳng hàng + một điểm ngoài hàng gửi SAI 1 mm. Phép biến đổi
        // tuyến tính đầy đủ (có xiên) có đủ bậc tự do để khớp đúng cả điểm sai → RMS ≈ 0, hiệu chuẩn
        // sai mà trông hoàn hảo. Lý do MinPoints mặc định là 9 (lưới 3×3, Chương 7, mục 7.7.2).
        WriteConfig("Cal-3", minPoints: 4);
        await Send("CS,Cal-3");
        foreach (string p in new[] { "1,18.000,26.000", "2,26.000,26.000", "3,34.000,26.000",
                     "4,19.000,32.000" })
        {
            (await Send("CP,Cal-3," + p)).Ok.Should().BeTrue();
        }

        CalibrationReply end = await Send("CE,Cal-3");
        _output.WriteLine($"CE: {end.Reason} RMS {string.Join(" ", end.Values)}");

        end.Ok.Should().BeTrue();
        double.Parse(end.Values[0], CultureInfo.InvariantCulture).Should().BeLessThan(0.01);
    }

    private Task<CalibrationReply> Send(string line)
    {
        MessageProtocol.TryParse(line, out Request? request, out _).Should().BeTrue();
        return _service.HandleAsync(request!);
    }

    private void WriteConfig(string id, int minPoints)
    {
        string folder = Path.Combine(_paths.MachineCalibration, id);
        Directory.CreateDirectory(folder);
        // Job tìm dấu = job vị trí đốm của sản phẩm mẫu (Chương 20): Blob, X/Y của đốm lớn nhất.
        File.Copy(Path.Combine(AppContext.BaseDirectory, "samples", "products", "P-1024", "jobs",
            "T2_spot.vpp"), Path.Combine(folder, "mark.vpp"));
        File.WriteAllText(Path.Combine(folder, CalibrationService.ConfigFile),
            "{ \"schemaVersion\": 1, \"camera\": \"TopCam\", \"markJob\": \"mark.vpp\", " +
            "\"minPoints\": " + minPoints + ", \"maxRmsPixels\": 0.5 }", Encoding.ASCII);
    }

    /// <summary>Nền sáng 220, một đốm tối 30×30 tâm (x, y) — Blob mặc định tìm vùng TỐI.</summary>
    private static void SaveSpot(string file, int cx, int cy)
    {
        const int w = 640, h = 480;
        CogImage8Grey image = new(w, h);
        ICogImage8PixelMemory memory = image.Get8GreyPixelMemory(CogImageDataModeConstants.Write,
            0, 0, w, h);
        try
        {
            byte[] row = new byte[w];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    row[x] = Math.Abs(x - cx) < 15 && Math.Abs(y - cy) < 15 ? (byte)20 : (byte)220;
                }
                Marshal.Copy(row, 0, memory.Scan0 + y * memory.Stride, w);
            }
        }
        finally
        {
            memory.Dispose();
        }
        using CogImageFile writer = new();
        writer.Open(file, CogImageFileModeConstants.Write);
        writer.Append(image);
        writer.Close();
    }

    private static CameraImage Load(string file)
    {
        using CogImageFile reader = new();
        reader.Open(file, CogImageFileModeConstants.Read);
        return new CogCameraImage(reader[0], file, DateTime.Now);
    }

    private static (double, double) LargestDark(CameraImage image)
    {
        using CogBlobTool tool = new() { InputImage = CogCameraImage.From(image) };
        tool.Run();
        CogBlobResult largest = tool.Results.GetBlobs().Cast<CogBlobResult>()
            .OrderByDescending(b => b.Area).First();
        return (largest.CenterOfMassX, largest.CenterOfMassY);
    }
}
