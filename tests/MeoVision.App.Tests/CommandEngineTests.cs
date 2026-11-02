using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MeoVision.App.Commands;
using MeoVision.App.Devices;
using MeoVision.App.Hosting;
using MeoVision.App.Products;
using MeoVision.Core.Commands;
using MeoVision.Core.Devices;
using MeoVision.Core.Localization;
using MeoVision.Core.Products;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MeoVision.App.Tests;

/// <summary>
/// Bộ máy chạy lệnh (Chương 20) với camera, đèn và job giả — kiểm thứ tự bước, chụp lại, thời
/// hạn, làn theo camera và chuyển sản phẩm khi đang chạy, không cần VisionPro.
/// </summary>
public sealed class CommandEngineTests : IDisposable
{
    private const string Product = @"{
  ""schemaVersion"": 1, ""id"": ""P-1"", ""name"": ""Engine test"",
  ""commands"": [
    { ""code"": ""T1"", ""name"": ""Top"", ""channel"": ""CH1"", ""camera"": ""TopCam"",
      ""job"": ""top.vpp"", ""calibration"": ""Cal-1"", ""exposureMs"": 2.0,
      ""timeoutMs"": 300, ""retry"": 1,
      ""lights"": [ { ""role"": ""MainLight"", ""channel"": 1, ""intensity"": 180 } ],
      ""outputs"": [ { ""terminal"": ""X"", ""name"": ""X"", ""format"": ""0.00"",
                       ""lower"": 0, ""upper"": 200, ""offset"": 0.5 } ] },
    { ""code"": ""T3"", ""name"": ""Bottom"", ""channel"": ""CH2"", ""camera"": ""BottomCam"",
      ""job"": ""bottom.vpp"", ""timeoutMs"": 1000,
      ""outputs"": [ { ""terminal"": ""X"", ""name"": ""X"", ""format"": ""0.00"" } ] }
  ]
}";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "mv-engine-" +
        Guid.NewGuid().ToString("N"));
    private readonly AppPaths _paths;
    private readonly FakeJobInspector _inspector = new();
    private readonly ConcurrentDictionary<string, Func<CameraImage, JobRun>> _behaviors = new();
    private readonly ScriptedCamera _top = new("TopCam");
    private readonly ScriptedCamera _bottom = new("BottomCam");
    private readonly ScriptedLight _light = new("MainLight");
    private readonly DeviceManager _devices;
    private readonly ProductStore _store;
    private readonly CommandEngine _engine;

    public CommandEngineTests()
    {
        _paths = AppPaths.Resolve(_root, Path.GetTempPath());
        _paths.EnsureCreated();
        WriteProduct("P-1");
        _inspector.JobFactory = name =>
        {
            FakeVisionJob job = new(name);
            job.Behavior = image => _behaviors.TryGetValue(name, out var run)
                ? run(image)
                : Accept(130.984);
            return job;
        };
        _devices = new DeviceManager(new DeviceManagerOptions(),
            NullLogger<DeviceManager>.Instance);
        _devices.Add(_top);
        _devices.Add(_bottom);
        _devices.Add(_light);
        _devices.OpenAllAsync().GetAwaiter().GetResult();
        _store = new ProductStore(_paths, new ProductLoader(_inspector, new FakeCalibrationLoader()),
            new ProductBackup(_paths), NullLogger<ProductStore>.Instance);
        _engine = new CommandEngine(_store, _devices, NullLogger<CommandEngine>.Instance);
    }

    public void Dispose()
    {
        _engine.Dispose();
        _devices.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task Ok_Command_Sets_Lights_Grabs_Runs_And_Formats_With_Invariant_Culture()
    {
        Switch("P-1");
        CultureInfo saved = CultureInfo.DefaultThreadCurrentCulture;
        CultureInfo.DefaultThreadCurrentCulture = new CultureInfo("vi-VN");   // dấu thập phân ","
        try
        {
            CommandResult result = await _engine.ExecuteAsync("CH1", "T1");

            result.Status.Should().Be(CommandStatus.Ok);
            result.Outputs.Single().Text.Should().Be("131.48");   // 130.984 + bù 0.5, dấu "."
            _light.Calls.Single().Should().Equal(new LightChannelSetting(1, 180));
            _top.Exposures.Single().Should().Be(2.0);
            result.Times.GrabAttempts.Should().Be(1);
        }
        finally
        {
            CultureInfo.DefaultThreadCurrentCulture = saved;
        }
    }

    [Fact]
    public async Task Unknown_Code_Wrong_Channel_And_No_Product_Are_Errors_Not_Exceptions()
    {
        (await _engine.ExecuteAsync("CH1", "T1")).Error!.Key.Should().Be("cmd.noProduct");
        Switch("P-1");

        CommandResult wrongChannel = await _engine.ExecuteAsync("CH2", "T1");

        wrongChannel.Status.Should().Be(CommandStatus.Error);
        wrongChannel.Error!.Key.Should().Be("cmd.unknown");
        _top.Grabs.Should().Be(0);
    }

    [Fact]
    public async Task A_Missed_Frame_Is_Retried_And_Does_Not_Fault_The_Camera()
    {
        Switch("P-1");
        _top.TransientFailures = 1;

        CommandResult result = await _engine.ExecuteAsync("CH1", "T1");

        result.Status.Should().Be(CommandStatus.Ok);
        result.Times.GrabAttempts.Should().Be(2);
        _devices.StatusOf("TopCam").Should().Be(DeviceStatus.Connected);
    }

    [Fact]
    public async Task A_Missed_Frame_Without_Retry_Left_Is_An_Error_But_Camera_Stays_Connected()
    {
        Switch("P-1");
        _bottom.TransientFailures = 1;   // T3: retry = 0

        CommandResult result = await _engine.ExecuteAsync("CH2", "T3");

        result.Error!.Key.Should().Be("cmd.grabFailed");
        _devices.StatusOf("BottomCam").Should().Be(DeviceStatus.Connected);
    }

    [Fact]
    public async Task Lights_That_Cannot_Be_Set_Stop_The_Command_Before_Grabbing()
    {
        Switch("P-1");
        _light.Fail = true;

        CommandResult result = await _engine.ExecuteAsync("CH1", "T1");

        result.Error!.Key.Should().Be("cmd.lightFailed");
        _top.Grabs.Should().Be(0);
    }

    [Fact]
    public async Task Slow_Job_Times_Out_And_The_Next_Command_Waits_For_It_Instead_Of_Overlapping()
    {
        Switch("P-1");
        _behaviors["top.vpp"] = _ => Slow(500);
        Stopwatch watch = Stopwatch.StartNew();

        CommandResult first = await _engine.ExecuteAsync("CH1", "T1");

        first.Error!.Key.Should().Be("cmd.timeout");
        watch.ElapsedMilliseconds.Should().BeLessThan(450);   // trả lời máy đúng hạn 300 ms
        _behaviors["top.vpp"] = _ => Accept(1);
        CommandResult second = await _engine.ExecuteAsync("CH1", "T1");
        second.Status.Should().Be(CommandStatus.Ok);
        second.Times.QueueMs.Should().BeGreaterThan(100);   // chờ job cũ chạy nốt
        FakeVisionJob job = _inspector.Created.Single(j => j.Name == "top.vpp");
        job.MaxConcurrent.Should().Be(1);
    }

    [Fact]
    public async Task Commands_Of_Two_Cameras_Run_In_Parallel()
    {
        _behaviors["top.vpp"] = _ => Slow(250);
        _behaviors["bottom.vpp"] = _ => Slow(250);
        WriteProduct("P-1", Product.Replace("\"timeoutMs\": 300", "\"timeoutMs\": 1000"));
        Switch("P-1");
        Stopwatch watch = Stopwatch.StartNew();

        CommandResult[] results = await Task.WhenAll(_engine.ExecuteAsync("CH1", "T1"),
            _engine.ExecuteAsync("CH2", "T3"));

        results.Should().OnlyContain(r => r.Status == CommandStatus.Ok);
        watch.ElapsedMilliseconds.Should().BeLessThan(450);   // nối tiếp sẽ là ≥ 500 ms
    }

    [Fact]
    public async Task Switching_Product_During_A_Command_Closes_The_Old_Job_Only_After_It()
    {
        WriteProduct("P-1", Product.Replace("\"timeoutMs\": 300", "\"timeoutMs\": 1000"));
        WriteProduct("P-2", Product.Replace("P-1", "P-2"));
        Switch("P-1");
        FakeVisionJob oldJob = _inspector.Created.Single(j => j.Name == "top.vpp");
        _behaviors["top.vpp"] = _ => Slow(300);

        Task<CommandResult> running = _engine.ExecuteAsync("CH1", "T1");
        await Task.Delay(100);
        Switch("P-2");

        oldJob.Disposed.Should().BeFalse();   // lệnh còn đang dùng nó
        (await running).Status.Should().Be(CommandStatus.Ok);
        for (int i = 0; i < 100 && !oldJob.Disposed; i++)
        {
            await Task.Delay(10);   // lease trả trong phần tiếp nối của lệnh
        }
        oldJob.Disposed.Should().BeTrue();
        _store.Current!.Id.Should().Be("P-2");
    }

    [Fact]
    public async Task Two_Camera_Command_Waits_For_Lights_Grabs_Both_And_Feeds_The_Extra_Terminal()
    {
        WriteProduct("P-1", Product
            .Replace("\"timeoutMs\": 300", "\"timeoutMs\": 1000")
            .Replace("\"intensity\": 180 }", "\"intensity\": 180, \"settleMs\": 150 }")
            .Replace("\"retry\": 1,", "\"retry\": 1, \"brightness\": 0.4, \"extraCameras\": [ " +
                     "{ \"camera\": \"BottomCam\", \"terminal\": \"SideImage\", \"exposureMs\": 4 } ],"));
        _inspector.Jobs["top.vpp"] = new JobInspection(null,
            new[] { new TerminalInfo("InputImage", typeof(object)),
                    new TerminalInfo("SideImage", typeof(object)) },
            new[] { new TerminalInfo("X", typeof(double)) }, null, 5);
        Switch("P-1");
        Stopwatch watch = Stopwatch.StartNew();

        CommandResult result = await _engine.ExecuteAsync("CH1", "T1");

        result.Status.Should().Be(CommandStatus.Ok);
        watch.ElapsedMilliseconds.Should().BeGreaterOrEqualTo(140);   // chờ đèn ổn định 150 ms
        _top.Grabs.Should().Be(1);
        _bottom.Exposures.Should().Equal(4.0);
        FakeVisionJob job = _inspector.Created.Last(j => j.Name == "top.vpp");
        job.LastRequest!.Images!.Keys.Should().Equal("SideImage");
        result.Frame!.Extras.Should().ContainSingle();
        _bottom.Images.Single().Disposed.Should().BeTrue();   // không ai Share: ảnh phụ trả cùng ảnh chính
    }

    [Fact]
    public async Task Bypassed_Command_Answers_Bypass_Without_Touching_Lights_Camera_Or_Job()
    {
        Switch("P-1");
        BypassService bypass = new();
        _engine.Bypass = bypass;
        bypass.Enable("T1", TimeSpan.FromHours(1), "camera lens broken", "engineer");

        CommandResult result = await _engine.ExecuteAsync("CH1", "T1", "SN-1");

        result.Status.Should().Be(CommandStatus.Bypassed);
        result.Error!.Key.Should().Be("cmd.bypassed");
        _top.Grabs.Should().Be(0);
        _light.Calls.Should().BeEmpty();
        bypass.Active.Single().Count.Should().Be(1);
        (await _engine.ExecuteAsync("CH2", "T3")).Status.Should().Be(CommandStatus.Ok);   // lệnh khác vẫn kiểm
    }

    [Fact]
    public async Task Guide_Command_Is_Never_Bypassed_Even_If_Bypass_Was_Turned_On()
    {
        // DESIGN_LOG [29]: lệnh dẫn hướng trả X, Y, góc cho chuyển động — không ai bịa được toạ độ.
        WriteProduct("P-1", Product.Replace("\"code\": \"T3\",", "\"code\": \"T3\", \"kind\": \"Guide\","));
        Switch("P-1");
        BypassService bypass = new();
        _engine.Bypass = bypass;
        bypass.Enable("T3", TimeSpan.FromHours(1), "camera lens broken", "engineer");

        CommandResult result = await _engine.ExecuteAsync("CH2", "T3");

        result.Status.Should().Be(CommandStatus.Ok);
        _bottom.Grabs.Should().Be(1);
        bypass.Active.Single().Count.Should().Be(0);
    }

    private void Switch(string id)
    {
        MachineView machine = new(new[] { "TopCam", "BottomCam" },
            new Dictionary<string, (int, int)> { ["MainLight"] = (4, 255) },
            new[] { "CH1", "CH2" }, calibration => calibration == "Cal-1");
        _store.Switch(id, machine, "test").Problems.Should().BeEmpty();
    }

    private void WriteProduct(string id, string json = Product)
    {
        ProductPaths product = _paths.Product(id);
        Directory.CreateDirectory(product.Jobs);
        File.WriteAllText(product.ProductFile, json, Encoding.UTF8);
        File.WriteAllText(product.Job("top.vpp"), "vpp");
        File.WriteAllText(product.Job("bottom.vpp"), "vpp");
    }

    private static JobRun Accept(double x) =>
        new(JobRunStatus.Accept, string.Empty, new Dictionary<string, object?> { ["X"] = x }, 1);

    private static JobRun Slow(int ms)
    {
        Thread.Sleep(ms);   // job VisionPro thật chạy đồng bộ và không huỷ được
        return Accept(1);
    }
}

/// <summary>Camera giả: đếm lần chụp, ghi exposure, hụt khung theo yêu cầu.</summary>
internal sealed class ScriptedCamera : ICamera
{
    public ScriptedCamera(string role) => Role = role;

    public string Role { get; }

    public string Description => "scripted";

    public int TransientFailures { get; set; }

    public int Grabs { get; private set; }

    public List<double?> Exposures { get; } = new();

    /// <summary>Mọi ảnh đã trả ra — để kiểm ảnh được Dispose đúng lúc (Chương 22).</summary>
    public List<ScriptedImage> Images { get; } = new();

    public Task OpenAsync(CancellationToken ct) => Task.CompletedTask;

    public Task<CameraImage> GrabAsync(CaptureSettings settings, CancellationToken ct)
    {
        Grabs++;
        Exposures.Add(settings.ExposureMs);
        if (TransientFailures > 0)
        {
            TransientFailures--;
            throw new DeviceException(Msg.Of("camera.grabFailed", Role, "missed"),
                transient: true);
        }
        ScriptedImage image = new();
        Images.Add(image);
        return Task.FromResult<CameraImage>(image);
    }

    public void Close()
    {
    }

    public void Dispose()
    {
    }

}

/// <summary>Ảnh giả biết mình đã bị Dispose chưa.</summary>
internal sealed class ScriptedImage : CameraImage
{
    public ScriptedImage()
        : base("scripted", 64, 48, DateTime.Now)
    {
    }

    public bool Disposed { get; private set; }

    protected override void Dispose(bool disposing) => Disposed = true;
}

/// <summary>Đèn giả: ghi mỗi lần đặt kênh; <see cref="Fail"/> = mất kết nối.</summary>
internal sealed class ScriptedLight : ILightController
{
    public ScriptedLight(string role) => Role = role;

    public string Role { get; }

    public string Description => "scripted";

    public int ChannelCount => 4;

    public int MaxIntensity => 255;

    public bool Fail { get; set; }

    public List<IReadOnlyList<LightChannelSetting>> Calls { get; } = new();

    public Task OpenAsync(CancellationToken ct) => Task.CompletedTask;

    public Task SetAsync(IReadOnlyList<LightChannelSetting> channels, CancellationToken ct)
    {
        if (Fail)
        {
            throw new IOException("connection closed");
        }
        Calls.Add(channels);
        return Task.CompletedTask;
    }

    public void Close()
    {
    }

    public void Dispose()
    {
    }
}
