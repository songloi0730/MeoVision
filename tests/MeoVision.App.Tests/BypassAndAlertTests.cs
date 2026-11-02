using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MeoVision.App.Alerts;
using MeoVision.App.Comm;
using MeoVision.App.Commands;
using MeoVision.App.Devices;
using MeoVision.App.Hosting;
using MeoVision.App.Results;
using MeoVision.Core.Devices;
using MeoVision.Core.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MeoVision.App.Tests;

/// <summary>
/// DESIGN_LOG [28]: bỏ qua có kiểm soát (D1) và dải cảnh báo (D3) — các chương trình tham khảo làm bỏ
/// qua bằng cờ im lặng và báo lỗi bằng hộp thoại; ở đây kiểm đúng hạn, đúng lý do, đúng nguồn.
/// </summary>
public sealed class BypassAndAlertTests
{
    private DateTime _now = new(2026, 10, 31, 8, 0, 0);

    [Fact]
    public void Bypass_Needs_A_Reason_And_A_Limited_Duration()
    {
        BypassService bypass = new(() => _now);

        bypass.Invoking(b => b.Enable("T1", TimeSpan.FromHours(1), "abc", "engineer"))
            .Should().Throw<ArgumentException>();
        bypass.Invoking(b => b.Enable("T1", TimeSpan.FromHours(13), "lens broken", "engineer"))
            .Should().Throw<ArgumentOutOfRangeException>();
        bypass.Invoking(b => b.Enable("T1", TimeSpan.Zero, "lens broken", "engineer"))
            .Should().Throw<ArgumentOutOfRangeException>();
        bypass.Active.Should().BeEmpty();
    }

    [Fact]
    public void Bypass_Counts_Uses_And_Expires_By_Itself_With_An_Event()
    {
        BypassService bypass = new(() => _now);
        List<BypassEntry> expired = new();
        bypass.Expired += (_, e) => expired.Add(e.Entry);
        bypass.Enable("t1", TimeSpan.FromHours(8), "camera lens broken", "engineer");

        bypass.TryUse("T1", out BypassEntry? entry).Should().BeTrue();   // mã không phân biệt hoa thường
        bypass.TryUse("T1", out _).Should().BeTrue();
        bypass.TryUse("T3", out _).Should().BeFalse();
        entry!.Count.Should().Be(2);

        _now = _now.AddHours(8);
        bypass.TryUse("T1", out _).Should().BeFalse();
        expired.Should().ContainSingle().Which.Count.Should().Be(2);
        bypass.Active.Should().BeEmpty();
    }

    [Fact]
    public void New_Service_Means_No_Bypass_After_Restart()
    {
        BypassService before = new(() => _now);
        before.Enable("T1", TimeSpan.FromHours(8), "camera lens broken", "engineer");

        new BypassService(() => _now).Active.Should().BeEmpty();   // chỉ sống trong bộ nhớ
    }

    [Fact]
    public void Alerts_List_Errors_First_Keep_Their_Start_Time_And_Clear()
    {
        AlertCenter alerts = new(() => _now);
        int changes = 0;
        alerts.Changed += (_, _) => changes++;

        alerts.Raise("bypass.T1", AlertLevel.Warning, Msg.Of("alert.bypass", "T1"));
        _now = _now.AddMinutes(5);
        alerts.Raise("device.TopCam", AlertLevel.Error, Msg.Of("device.notConnected", "TopCam"));
        alerts.Raise("bypass.T1", AlertLevel.Warning, Msg.Of("alert.bypass", "T1", 3));

        alerts.Active.Select(a => a.Id).Should().Equal("device.TopCam", "bypass.T1");
        alerts.Active.Single(a => a.Id == "bypass.T1").Since.Should().Be(_now.AddMinutes(-5));
        alerts.Clear("device.TopCam");
        alerts.ClearWhere(id => id.StartsWith("bypass.", StringComparison.Ordinal));
        alerts.Active.Should().BeEmpty();
        changes.Should().Be(5);
    }

    [Fact]
    public async Task Feeder_Shows_Faulted_Device_Bypass_And_Saving_Failures_Then_Clears_Them()
    {
        AlertCenter alerts = new(() => _now);
        BypassService bypass = new(() => _now);
        using DeviceManager devices = new(new DeviceManagerOptions(), NullLogger<DeviceManager>.Instance);
        ScriptedCamera camera = new("TopCam");
        devices.Add(camera);
        await devices.OpenAllAsync();
        RecorderCounters counters = new(0, 0, 0, 0);
        using AlertFeeder feeder = new(alerts, devices, bypass, () => Array.Empty<IChannel>(),
            () => counters);

        bypass.Enable("T1", TimeSpan.FromHours(1), "camera lens broken", "engineer");
        alerts.Active.Should().ContainSingle(a => a.Id == "bypass.T1" && a.Level == AlertLevel.Warning);

        feeder.Tick(_now);
        counters = new RecorderCounters(10, 2, 1, 0);   // một ảnh bị bỏ
        feeder.Tick(_now);
        alerts.Active.Should().Contain(a => a.Id == "results.saving" && a.Level == AlertLevel.Error);

        _now = _now.AddHours(2);   // bỏ qua hết hạn, 2 giờ không còn lỗi lưu
        feeder.Tick(_now);
        alerts.Active.Should().BeEmpty();

        feeder.FromStartup(new StartupReport(new[]
        {
            new StepResult("startup.step.machine", true, StepStatus.Failed, Msg.Of("x"), 1),
            new StepResult("startup.step.users", false, StepStatus.Warning, Msg.Of("y"), 1),
            new StepResult("startup.step.storage", false, StepStatus.Warning, Msg.Of("z"), 1),
        }));
        alerts.Active.Select(a => a.Id).Should().Equal("startup.startup.step.machine",
            "startup.startup.step.storage");   // cảnh báo mật khẩu mặc định không lên dải
    }
}
