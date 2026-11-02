using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cognex.VisionPro;
using Cognex.VisionPro.Blob;
using Cognex.VisionPro.CalibFix;
using Cognex.VisionPro.ImageFile;
using Cognex.VisionPro.Implementation.Internal;   // CogScriptSupport, CogReferencedAssembly
using Cognex.VisionPro.ToolBlock;
using FluentAssertions;
using MeoVision.App.Commands;
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
/// Chương 20 chạy THẬT trên VisionPro: ba hành vi đã đo bằng spike (output cũ, một job hai luồng,
/// hiệu chuẩn) và sản phẩm mẫu chạy trọn đèn → chụp → hiệu chuẩn → job → kết luận.
/// </summary>
public sealed class CommandRunTests : IDisposable
{
    private static readonly string Images = @"C:\Program Files\Cognex\VisionPro\Images";

    // Job kiểu hai hệ thật hay viết: chỉ gán output khi tìm thấy đủ đốm (C# 5 — Chương 12).
    private const string PartialScript = @"
using System;
using Cognex.VisionPro;
using Cognex.VisionPro.ToolBlock;
using Cognex.VisionPro.Blob;
public class CogToolBlockAdvancedScript : CogToolBlockAdvancedScriptBase
{
  private CogToolBlock mToolBlock;
  public override void Initialize(Cognex.VisionPro.ToolGroup.CogToolGroup host)
  { base.Initialize(host); mToolBlock = (CogToolBlock)host; }
  public override void ModifyCurrentRunRecord(ICogRecord currentRecord) { }
  public override void ModifyLastRunRecord(ICogRecord lastRecord) { }
  public override bool GroupRun(ref string message, ref CogToolResultConstants result)
  {
    CogBlobTool b = (CogBlobTool)mToolBlock.Tools[""Blob""];
    b.InputImage = (CogImage8Grey)mToolBlock.Inputs[""InputImage""].Value;
    mToolBlock.RunTool(b, ref message, ref result);
    if (b.Results != null && b.Results.GetBlobs().Count > 5)
      mToolBlock.Outputs[""Count""].Value = b.Results.GetBlobs().Count;
    return false;
  }
}";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "mv-run-" +
        Guid.NewGuid().ToString("N"));
    private readonly ITestOutputHelper _output;

    public CommandRunTests(ITestOutputHelper output)
    {
        _output = output;
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void Output_Left_Over_From_The_Previous_Run_Is_Cleared_Before_Each_Run()
    {
        using CogVisionJob job = new(PartialJob(), "partial.vpp", JobContract.InputImage);
        using CameraImage blobs = Blobs();
        using CameraImage oneSpot = new CogCameraImage(OneSpot(640, 480), "one spot",
            DateTime.Now);

        JobRun good = job.Run(blobs, new[] { "Count" });
        JobRun empty = job.Run(oneSpot, new[] { "Count" });

        good.Outputs["Count"].Should().Be(29);
        // Không xoá trước: Count vẫn là 29 kèm Accept (đo ở spike). Có xoá: null → bộ kết luận
        // báo lỗi "job không gán output", không gửi số của sản phẩm trước.
        empty.Status.Should().Be(JobRunStatus.Accept);
        empty.Outputs["Count"].Should().BeNull();
    }

    [Fact]
    public async Task One_Job_Shared_By_Two_Threads_Always_Gives_The_Right_Answer()
    {
        using CogVisionJob job = new(PartialJob(), "partial.vpp", JobContract.InputImage);
        using CameraImage blobs = Blobs();
        int wrong = 0;

        Task Loop() => Task.Factory.StartNew(() =>
        {
            for (int i = 0; i < 100; i++)
            {
                if (!Equals(job.Run(blobs, new[] { "Count" }).Outputs["Count"], 29))
                {
                    Interlocked.Increment(ref wrong);
                }
            }
        }, TaskCreationOptions.LongRunning);
        await Task.WhenAll(Loop(), Loop());

        wrong.Should().Be(0);   // không khoá: 9/200 sai (spike)
    }

    [Fact]
    public void Calibration_Makes_The_Job_Report_Millimetres()
    {
        string folder = Path.Combine(_root, "calibration");
        SaveCalibration(Path.Combine(folder, "Cal-1"), calibrate: true);
        CalibrationLoad load = new CogCalibrationLoader(folder).Load("Cal-1");
        using CameraImage pixels = Blobs();
        using CameraImage calibrated = Blobs();

        load.Error.Should().BeNull();
        using ICalibration calibration = load.Calibration!;
        Stopwatch watch = Stopwatch.StartNew();
        calibration.Apply(calibrated);
        _output.WriteLine($"Apply: {watch.Elapsed.TotalMilliseconds:0.000} ms");

        double xPixel = LargestBlobX(pixels);
        double xMm = LargestBlobX(calibrated);
        xMm.Should().BeApproximately(10 + 0.05 * xPixel, 1e-6);
        CogCameraImage.From(calibrated).SelectedSpaceName.Should().Be(@"@\Calibrated");
    }

    [Fact]
    public void Uncalibrated_Tool_Or_Wrong_File_Is_Refused_With_A_Reason()
    {
        string folder = Path.Combine(_root, "calibration");
        SaveCalibration(Path.Combine(folder, "Raw"), calibrate: false);
        Directory.CreateDirectory(Path.Combine(folder, "Block"));
        CogSerializer.SaveObjectToFile(new CogToolBlock(),
            Path.Combine(folder, "Block", CogCalibrationLoader.FileName));
        CogCalibrationLoader loader = new(folder);

        loader.Load("Raw").Error!.Key.Should().Be("calibration.notCalibrated");
        loader.Load("Block").Error!.Key.Should().Be("calibration.wrongType");
        loader.Load("None").Error!.Key.Should().Be("calibration.missing");
    }

    [Fact]
    public async Task Sample_Product_Runs_End_To_End_On_Real_VisionPro()
    {
        AppPaths paths = AppPaths.Resolve(Path.Combine(_root, "data"), Path.GetTempPath());
        paths.EnsureCreated();
        CopyFolder(Path.Combine(AppContext.BaseDirectory, "samples", "products", "P-1024"),
            paths.Product("P-1024").Directory);
        CopyFolder(Path.Combine(AppContext.BaseDirectory, "samples", "machine", "calibration"),
            paths.MachineCalibration);
        string images = Path.Combine(_root, "images");
        Directory.CreateDirectory(images);
        File.Copy(Path.Combine(Images, "blobs.bmp"), Path.Combine(images, "blobs.bmp"));
        File.SetAttributes(Path.Combine(images, "blobs.bmp"), FileAttributes.Normal);

        using DeviceManager devices = new(new DeviceManagerOptions(),
            NullLogger<DeviceManager>.Instance);
        devices.Add(new FolderCamera("TopCam", images, new[] { "*.bmp" }, loop: true, toGrey: true));
        devices.Add(new FolderCamera("BottomCam", images, new[] { "*.bmp" }, loop: true,
            toGrey: true));
        devices.Add(new NullLight("MainLight"));
        await devices.OpenAllAsync();
        ProductStore store = new(paths, new ProductLoader(new CogJobInspector(),
                new CogCalibrationLoader(paths.MachineCalibration)),
            new ProductBackup(paths), NullLogger<ProductStore>.Instance);
        MachineView machine = new(devices.CameraRoles,
            devices.LightRoles.ToDictionary(r => r, devices.LightLimits), new[] { "CH1", "CH2" },
            id => Directory.Exists(Path.Combine(paths.MachineCalibration, id)));
        store.Switch("P-1024", machine, "test").Problems.Should().BeEmpty();
        using CommandEngine engine = new(store, devices, NullLogger<CommandEngine>.Instance);

        List<CommandResult> results = new();
        for (int round = 0; round < 5; round++)
        {
            results.Add(await engine.ExecuteAsync("CH1", "T1"));
            results.Add(await engine.ExecuteAsync("CH1", "T2"));
            results.Add(await engine.ExecuteAsync("CH2", "T3"));
        }
        foreach (CommandResult r in results)
        {
            StepTimes t = r.Times;
            _output.WriteLine($"{r.Code} {r.Status} total={t.TotalMs} light={t.LightMs} " +
                $"grab={t.GrabMs} calib={t.CalibrationMs} run={t.RunMs} " +
                string.Join(" ", r.Outputs.Select(o => o.Name + "=" + o.Text)));
        }
        store.Close();

        results.Should().OnlyContain(r => r.Status == CommandStatus.Ok);
        results.Last(r => r.Code == "T1").Outputs.Single().Text.Should().Be("131.0");
        results.Last(r => r.Code == "T3").Outputs.Single().Text.Should().Be("29");
        double spotX = results.Last(r => r.Code == "T2").Outputs[0].Value!.Value;
        spotX.Should().BeInRange(10, 10 + 0.05 * 640);   // mm, không phải pixel
    }

    private static CogToolBlock PartialJob()
    {
        CogToolBlock block = new();
        block.Inputs.Add(new CogToolBlockTerminal("InputImage", typeof(CogImage8Grey)));
        block.Outputs.Add(new CogToolBlockTerminal("Count", typeof(int)));
        CogBlobTool blob = new() { Name = "Blob" };
        blob.RunParams.ConnectivityMinPixels = 20;
        block.Tools.Add(blob);
        block.CreateNewScript(CogScriptLanguageConstants.ScriptCSharp,
            CogToolBlockScriptTypeConstants.Advanced);
        ArrayList references = block.Script.References;
        block.Script = new CogScriptSupport(PartialScript, references,
            CogScriptLanguageConstants.ScriptCSharp);
        return block;
    }

    /// <summary>
    /// Nền đen + MỘT đốm trắng 40×40: Blob chạy được (ảnh đều màu thì Blob báo lỗi ngưỡng động) và
    /// ra 1 đốm. Phải tự tô cả ảnh: <c>new CogImage8Grey(w, h)</c> không xoá bộ nhớ điểm ảnh về 0
    /// (đo: 40/40 ảnh có pixel rác).
    /// </summary>
    private static CogImage8Grey OneSpot(int width, int height)
    {
        CogImage8Grey image = new(width, height);
        // Có Dispose() nhưng không cài IDisposable — không dùng được "using" (Chương 18).
        ICogImage8PixelMemory memory = image.Get8GreyPixelMemory(
            CogImageDataModeConstants.Write, 0, 0, width, height);
        try
        {
            byte[] row = new byte[width];
            for (int y = 0; y < height; y++)
            {
                bool spotRow = y >= 200 && y < 240;
                for (int x = 0; x < width; x++)
                {
                    row[x] = spotRow && x >= 300 && x < 340 ? (byte)255 : (byte)0;
                }
                System.Runtime.InteropServices.Marshal.Copy(row, 0,
                    memory.Scan0 + y * memory.Stride, width);
            }
        }
        finally
        {
            memory.Dispose();
        }
        return image;
    }

    private static CameraImage Blobs()
    {
        using CogImageFile file = new();
        file.Open(Path.Combine(Images, "blobs.bmp"), CogImageFileModeConstants.Read);
        return new CogCameraImage(file[0], "blobs.bmp", DateTime.Now);
    }

    private static double LargestBlobX(CameraImage image)
    {
        using CogBlobTool tool = new() { InputImage = CogCameraImage.From(image) };
        tool.RunParams.ConnectivityMinPixels = 20;
        tool.Run();
        return tool.Results.GetBlobs().Cast<CogBlobResult>().OrderByDescending(b => b.Area)
            .First().CenterOfMassX;
    }

    private static void SaveCalibration(string folder, bool calibrate)
    {
        Directory.CreateDirectory(folder);
        CogCalibNPointToNPoint calibration = new();
        foreach (double x in new double[] { 0, 1000 })
        {
            foreach (double y in new double[] { 0, 1000 })
            {
                calibration.AddPointPair(x, y, 10 + 0.05 * x, 20 + 0.05 * y);
            }
        }
        if (calibrate)
        {
            calibration.Calibrate();
        }
        using CogCalibNPointToNPointTool tool = new() { Calibration = calibration };
        CogSerializer.SaveObjectToFile(tool, Path.Combine(folder, CogCalibrationLoader.FileName));
    }

    private static void CopyFolder(string source, string target)
    {
        foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            string to = Path.Combine(target, file.Substring(source.Length + 1));
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Copy(file, to, overwrite: true);
        }
    }

    /// <summary>Đèn không làm gì — máy dev không có bộ điều khiển đèn.</summary>
    private sealed class NullLight : ILightController
    {
        public NullLight(string role) => Role = role;

        public string Role { get; }

        public string Description => "none";

        public int ChannelCount => 4;

        public int MaxIntensity => 255;

        public Task OpenAsync(CancellationToken ct) => Task.CompletedTask;

        public Task SetAsync(IReadOnlyList<LightChannelSetting> channels, CancellationToken ct) =>
            Task.CompletedTask;

        public void Close()
        {
        }

        public void Dispose()
        {
        }
    }
}
