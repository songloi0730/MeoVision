using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MeoVision.App.Comm;
using MeoVision.App.Comm.Plc;
using MeoVision.App.Commands;
using MeoVision.App.Devices;
using MeoVision.App.Hosting;
using MeoVision.App.Results;
using MeoVision.Core;
using MeoVision.Core.Devices;
using MeoVision.Core.Localization;

namespace MeoVision.App.Alerts;

/// <summary>
/// Nối các nguồn tình trạng vào <see cref="AlertCenter"/> (DESIGN_LOG [28] D3): thiết bị mất kết nối,
/// kênh PLC mất, lệnh đang bỏ qua, lưu ảnh/kết quả đang hỏng, bước khởi động hỏng. Màn chính gọi
/// <see cref="Tick"/> mỗi giây cho những thứ không có sự kiện (hết hạn bỏ qua, kênh PLC, bộ đếm lỗi).
/// </summary>
public sealed class AlertFeeder : IDisposable
{
    /// <summary>Không có lỗi lưu mới trong chừng này thì coi như đã ổn, xoá cảnh báo.</summary>
    public static readonly TimeSpan SaveFailureQuietTime = TimeSpan.FromMinutes(10);

    /// <summary>Bước khởi động mà CẢNH BÁO của nó đáng nằm trên dải (còn lại chỉ ở nhật ký).</summary>
    private static readonly HashSet<string> WarningSteps = new(StringComparer.Ordinal)
    {
        "startup.step.dataFolder", "startup.step.programFiles", "startup.step.storage",
    };

    private readonly AlertCenter _alerts;
    private readonly DeviceManager _devices;
    private readonly BypassService _bypass;
    private readonly Func<IReadOnlyList<IChannel>> _channels;
    private readonly Func<RecorderCounters?> _recorder;
    private RecorderCounters? _lastCounters;
    private DateTime _lastSaveFailure = DateTime.MinValue;

    public AlertFeeder(AlertCenter alerts, DeviceManager devices, BypassService bypass,
        Func<IReadOnlyList<IChannel>> channels, Func<RecorderCounters?> recorder)
    {
        Guard.ThrowIfNull(alerts, nameof(alerts));
        Guard.ThrowIfNull(devices, nameof(devices));
        Guard.ThrowIfNull(bypass, nameof(bypass));
        Guard.ThrowIfNull(channels, nameof(channels));
        Guard.ThrowIfNull(recorder, nameof(recorder));
        _alerts = alerts;
        _devices = devices;
        _bypass = bypass;
        _channels = channels;
        _recorder = recorder;
        _devices.StatusChanged += OnDeviceStatus;
        _bypass.Changed += OnBypassChanged;
        foreach (string role in _devices.Roles)
        {
            ShowDevice(role, _devices.StatusOf(role), null);
        }
    }

    /// <summary>Bước khởi động hỏng → lỗi; cảnh báo của vài bước chọn lọc → cảnh báo.</summary>
    public void FromStartup(StartupReport report)
    {
        Guard.ThrowIfNull(report, nameof(report));
        foreach (StepResult step in report.Steps)
        {
            string id = "startup." + step.NameKey;
            if (step.Status == StepStatus.Failed)
            {
                _alerts.Raise(id, AlertLevel.Error, step.Message);
            }
            else if (step.Status == StepStatus.Warning && WarningSteps.Contains(step.NameKey))
            {
                _alerts.Raise(id, AlertLevel.Warning, step.Message);
            }
        }
    }

    /// <summary>Gọi mỗi giây từ màn chính.</summary>
    public void Tick(DateTime now)
    {
        _bypass.ExpireOld();
        ShowBypass();
        foreach (IChannel channel in _channels())
        {
            // Kênh TCP không có client là chuyện bình thường (máy chưa nối); PLC thì phải luôn nối.
            if (channel is PlcChannel && channel.Connections == 0)
            {
                _alerts.Raise("channel." + channel.Id, AlertLevel.Error,
                    Msg.Of("alert.plcDisconnected", channel.Id, channel.Description));
            }
            else
            {
                _alerts.Clear("channel." + channel.Id);
            }
        }
        RecorderCounters? counters = _recorder();
        if (counters is not null)
        {
            if (_lastCounters is not null &&
                (counters.Failures > _lastCounters.Failures ||
                 counters.DroppedImages > _lastCounters.DroppedImages))
            {
                _lastSaveFailure = now;
                _alerts.Raise("results.saving", AlertLevel.Error, Msg.Of("alert.savingFailed",
                    counters.Failures, counters.DroppedImages));
            }
            else if (now - _lastSaveFailure > SaveFailureQuietTime)
            {
                _alerts.Clear("results.saving");
            }
            _lastCounters = counters;
        }
    }

    public void Dispose()
    {
        _devices.StatusChanged -= OnDeviceStatus;
        _bypass.Changed -= OnBypassChanged;
    }

    private void OnDeviceStatus(object? sender, DeviceStatusEventArgs e) =>
        ShowDevice(e.Role, e.Status, e.Reason);

    private void ShowDevice(string role, DeviceStatus status, Msg? reason)
    {
        if (status == DeviceStatus.Connected)
        {
            _alerts.Clear("device." + role);
        }
        else if (status == DeviceStatus.Faulted)
        {
            _alerts.Raise("device." + role, AlertLevel.Error,
                reason is null || reason.Key.Length == 0 ? Msg.Of("device.notConnected", role) : reason);
        }
    }

    private void OnBypassChanged(object? sender, EventArgs e) => ShowBypass();

    private void ShowBypass()
    {
        IReadOnlyList<BypassEntry> active = _bypass.Active;
        _alerts.ClearWhere(id => id.StartsWith("bypass.", StringComparison.Ordinal) &&
                                 !active.Any(b => id == "bypass." + b.Code));
        foreach (BypassEntry b in active)
        {
            _alerts.Raise("bypass." + b.Code, AlertLevel.Warning, Msg.Of("alert.bypass", b.Code,
                b.Until.ToString("HH:mm", CultureInfo.InvariantCulture), b.Count, b.User, b.Reason));
        }
    }
}
