using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MeoVision.App.Calibration;
using MeoVision.App.Comm;
using MeoVision.App.Devices;
using MeoVision.App.Hosting;
using MeoVision.App.Products;
using MeoVision.Core.Calibration;
using MeoVision.Core.Commands;
using MeoVision.Core.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MeoVision.App.Tests;

/// <summary>Bộ máy chạy lệnh giả: ghi lại lệnh được gọi, trả kết quả dựng sẵn.</summary>
internal sealed class FakeRunner : ICommandRunner
{
    public List<string> Calls { get; } = new();

    public Func<string, CommandResult> Result { get; set; } = code => new CommandResult(code,
        CommandStatus.Ok, null, new[] { new OutputValue("X", 1.23, "1.23", true),
            new OutputValue("Y", 4.56, "4.56", true) }, StepTimes.None);

    public Task<CommandResult> ExecuteAsync(string channel, string code, string serial = "",
        CancellationToken ct = default)
    {
        Calls.Add(channel + ":" + code);
        return Task.FromResult(Result(code));
    }
}

internal sealed class FakeCalibrationBuilder : ICalibrationBuilder
{
    public CalibrationFit Fit(IReadOnlyList<CalibrationPoint> points, string outputFile) =>
        CalibrationFit.Failed(Msg.Of("calibration.fitFailed", "fake"));
}

/// <summary>Quy tắc bản tin (Chương 21, mục 21.2) — không mạng, không camera.</summary>
public sealed class CommandServiceTests : IDisposable
{
    private const string Product = @"{
  ""schemaVersion"": 1, ""id"": ""P-1"", ""name"": ""Protocol test"",
  ""commands"": [
    { ""code"": ""T1"", ""channel"": ""CH1"", ""camera"": ""TopCam"", ""job"": ""a.vpp"",
      ""outputs"": [ { ""terminal"": ""X"" }, { ""terminal"": ""Y"" } ] },
    { ""code"": ""T10"", ""channel"": ""CH1"", ""camera"": ""TopCam"", ""job"": ""a.vpp"",
      ""outputs"": [ { ""terminal"": ""X"" } ] },
    { ""code"": ""T3"", ""channel"": ""CH2"", ""camera"": ""TopCam"", ""job"": ""a.vpp"",
      ""outputs"": [ { ""terminal"": ""X"" } ] }
  ]
}";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "mv-proto-" +
        Guid.NewGuid().ToString("N"));
    private readonly AppPaths _paths;
    private readonly ProductStore _store;
    private readonly FakeRunner _runner = new();
    private readonly CommandService _service;
    private readonly DeviceManager _devices = new(new DeviceManagerOptions(),
        NullLogger<DeviceManager>.Instance);

    public CommandServiceTests()
    {
        _paths = AppPaths.Resolve(_root, Path.GetTempPath());
        _paths.EnsureCreated();
        Write("P-1", Product);
        Write("P-2", Product.Replace("P-1", "P-2"));
        FakeJobInspector inspector = new();
        _store = new ProductStore(_paths, new ProductLoader(inspector, new FakeCalibrationLoader()),
            new ProductBackup(_paths), NullLogger<ProductStore>.Instance);
        _store.Switch("P-1", Machine(), "test").IsValid.Should().BeTrue();
        CalibrationService calibration = new(_paths, _devices, inspector,
            new FakeCalibrationBuilder(), _store, Machine, NullLogger<CalibrationService>.Instance);
        _service = new CommandService(_runner, _store, calibration, Machine,
            NullLogger<CommandService>.Instance);
    }

    public void Dispose()
    {
        _devices.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private Task<string> Send(string line, string channel = "CH1") => _service.HandleAsync(channel, line);

    /// <summary>Lý do của bản tin vừa trả — không gửi máy (DESIGN_LOG [29]) nhưng vẫn ghi lại.</summary>
    private string LastReason() => _service.Log.Since(0).Last().Reason;

    [Fact]
    public async Task Ok_Reply_Has_Code_Serial_Status_Empty_Reason_And_Every_Value()
    {
        (await Send("T1,P-1,SN88213")).Should().Be("T1,SN88213,OK,,1.23,4.56");
    }

    [Fact]
    public async Task T10_Runs_T10_Not_T1_Because_The_Whole_Code_Is_Matched()
    {
        await Send("T10,P-1,SN1");

        _runner.Calls.Should().Equal("CH1:T10");
    }

    [Fact]
    public async Task Ng_And_Errors_Keep_The_Same_Number_Of_Fields()
    {
        _runner.Result = code => new CommandResult(code, CommandStatus.Ng, null,
            new[] { new OutputValue("X", 1, "1.00", true), new OutputValue("Y", 9, "9.00", false) },
            StepTimes.None);
        (await Send("T1,P-1,SN1")).Should().Be("T1,SN1,NG,,1.00,9.00");   // lý do không gửi máy
        LastReason().Should().Be("LIMIT");

        _runner.Result = code => CommandResult.Failed(code, Msg.Of("cmd.grabFailed", "TopCam", "x"),
            StepTimes.None);
        (await Send("T1,P-1,SN1")).Should().Be("T1,SN1,ERR,,,");
        LastReason().Should().Be("CAMERA");
    }

    [Fact]
    public async Task Wrong_Product_Wrong_Channel_And_Unknown_Code_Are_Refused_Without_Running()
    {
        (await Send("T1,P-9,SN1")).Should().Be("T1,SN1,ERR,,,");
        LastReason().Should().Be("PRODUCT");
        (await Send("T3,P-1,SN1", channel: "CH1")).Should().Be("T3,SN1,ERR,,");
        LastReason().Should().Be("CHANNEL");
        (await Send("T7,P-1,SN1")).Should().Be("T7,SN1,ERR,");
        LastReason().Should().Be("UNKNOWN");
        _runner.Calls.Should().BeEmpty();
    }

    [Theory]
    [InlineData("t1,P-1,SN1")]       // mã lệnh phân biệt hoa/thường
    [InlineData("T1,P-1,SNé")]  // ký tự ngoài ASCII
    [InlineData("")]
    public async Task Malformed_Messages_Get_Bad_Format(string line)
    {
        (await Send(line)).Should().Be(",,ERR,");
        LastReason().Should().Be("BAD_FORMAT");
    }

    [Fact]
    public async Task Offline_Answers_At_Once_But_Heartbeat_Still_Works()
    {
        _service.Online = false;

        (await Send("T1,P-1,SN1")).Should().Be("T1,SN1,ERR,,,");
        LastReason().Should().Be("OFFLINE");
        (await Send("HB")).Should().Be("HB,,OK,");
        _runner.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Switch_Product_Checks_First_And_Keeps_The_Old_One_On_Error()
    {
        (await Send("SW,P-404")).Should().Be("SW,P-404,ERR,,1");
        LastReason().Should().Be("INVALID");
        _store.Current!.Id.Should().Be("P-1");

        (await Send("SW,P-2")).Should().Be("SW,P-2,OK,");
        _store.Current!.Id.Should().Be("P-2");
    }

    [Fact]
    public async Task A_Comma_Inside_A_Text_Value_Cannot_Shift_The_Fields()
    {
        _runner.Result = code => new CommandResult(code, CommandStatus.Ok, null,
            new[] { new OutputValue("Code", null, "AB,12", true), new OutputValue("Y", 1, "1", true) },
            StepTimes.None);

        (await Send("T1,P-1,SN1")).Should().Be("T1,SN1,OK,,AB_12,1");
    }

    [Fact]
    public async Task Calibration_Point_Without_Start_Is_Refused()
    {
        (await Send("CP,Cal-1,1,10.0,20.0")).Should().Be("CP,Cal-1,ERR,");
        LastReason().Should().Be("NO_SESSION");
    }

    [Fact]
    public async Task Bypassed_Result_Is_Sent_As_Byp_With_Every_Field_Empty()
    {
        _runner.Result = code => CommandResult.Bypassed(code, Msg.Of("cmd.bypassed", "engineer", "lens"),
            StepTimes.None);

        (await Send("T1,P-1,SN7")).Should().Be("T1,SN7,BYP,,,");
        LastReason().Should().Be("BYPASS");
    }

    [Fact]
    public async Task Every_Message_Is_Logged_And_Test_Messages_Run_Only_Offline()
    {
        await Send("T1,P-1,SN1");
        await _service.Invoking(s => s.TestAsync("CH1", "T1,P-1,TEST"))
            .Should().ThrowAsync<InvalidOperationException>();

        _service.Online = false;
        (await Send("T1,P-1,SN2")).Should().Contain(",ERR,");   // máy vẫn bị từ chối
        LastReason().Should().Be("OFFLINE");
        (await _service.TestAsync("CH1", "T1,P-1,TEST")).Should().Be("T1,TEST,OK,,1.23,4.56");

        IReadOnlyList<CommEntry> log = _service.Log.Since(0);
        log.Select(e => (e.Request, e.Test)).Should().Equal(("T1,P-1,SN1", false),
            ("T1,P-1,SN2", false), ("T1,P-1,TEST", true));
        _service.Log.Since(log[1].Index).Should().ContainSingle();
    }

    [Fact]
    public void Calibration_Form_Lists_Points_Rms_History_And_Restores_An_Old_Version()
    {
        string folder = Path.Combine(_paths.MachineCalibration, "CAL1");
        Directory.CreateDirectory(Path.Combine(folder, "history"));
        File.WriteAllText(Path.Combine(folder, CalibrationService.ConfigFile),
            "{ \"schemaVersion\": 1, \"camera\": \"TopCam\", \"maxRmsPixels\": 0.5 }");
        File.WriteAllText(Path.Combine(folder, CalibrationService.ResultFile), "current");
        File.WriteAllText(Path.Combine(folder, "history", "calibration_20261030_120000.vpp"), "old");
        File.WriteAllText(Path.Combine(folder, "points.csv"),
            "index,pixel_x,pixel_y,machine_x,machine_y,residual_px\r\n" +
            "1,10.000,20.000,0.000,0.000,0.300\r\n2,30.000,20.000,1.000,0.000,0.400\r\n");
        using CalibrationService calibration = new(_paths, _devices, new FakeJobInspector(),
            new FakeCalibrationBuilder(), _store, Machine, NullLogger<CalibrationService>.Instance);

        CalibrationInfo info = calibration.List().Single();
        info.Camera.Should().Be("TopCam");
        info.LastPoints.Should().Be(2);
        info.LastRms.Should().BeApproximately(Math.Sqrt((0.09 + 0.16) / 2), 1e-9);
        info.HistoryCount.Should().Be(1);
        info.PointsInSession.Should().BeNull();
        calibration.Points("CAL1").Select(r => r.Residual).Should().Equal(0.3, 0.4);

        calibration.RestoreFromHistory("CAL1", "calibration_20261030_120000.vpp", "engineer")
            .Should().BeNull();

        File.ReadAllText(Path.Combine(folder, CalibrationService.ResultFile)).Should().Be("old");
        calibration.History("CAL1").Should().HaveCount(2);   // bản vừa thay được cất lại
        calibration.RestoreFromHistory("CAL1", "missing.vpp", "engineer")!.Key
            .Should().Be("calibration.noHistory");
        calibration.CancelSession("CAL1").Should().BeFalse();
    }

    private static MachineView Machine() => new(new[] { "TopCam" },
        new Dictionary<string, (int, int)>(), new[] { "CH1", "CH2" }, _ => false);

    private void Write(string id, string json)
    {
        ProductPaths p = _paths.Product(id);
        Directory.CreateDirectory(p.Jobs);
        File.WriteAllText(p.ProductFile, json, Encoding.UTF8);
        File.WriteAllText(p.Job("a.vpp"), "vpp");
    }
}
