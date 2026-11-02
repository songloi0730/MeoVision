using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MeoVision.Core.Localization;

namespace MeoVision.Core.Devices;

/// <summary>Trạng thái thiết bị như <c>DeviceManager</c> nhìn thấy (Chương 18, mục 18.5).</summary>
public enum DeviceStatus
{
    Closed,
    Connected,
    Faulted,
}

/// <summary>
/// Thiết bị phần cứng mở/đóng được. Lớp cài đặt KHÔNG tự lo timeout hay kết nối lại — việc đó
/// là của <c>DeviceManager</c>, làm giống nhau cho mọi loại thiết bị.
/// </summary>
public interface IDevice : IDisposable
{
    /// <summary>Vai trò trong <c>machine.json</c>, ví dụ "TopCam".</summary>
    string Role { get; }

    /// <summary>Mô tả thiết bị thật để ghi log, ví dụ "GigE SN 24130517".</summary>
    string Description { get; }

    Task OpenAsync(CancellationToken ct);

    void Close();
}

/// <summary>Camera: chụp một ảnh theo thiết lập của lệnh đang chạy.</summary>
public interface ICamera : IDevice
{
    Task<CameraImage> GrabAsync(CaptureSettings settings, CancellationToken ct);
}

/// <summary>
/// Thiết lập chụp do LỆNH quyết định (Chương 19–20), không do camera. null = giữ nguyên giá trị
/// hiện tại của camera.
/// </summary>
public sealed record CaptureSettings(double? ExposureMs = null, double? Brightness = null,
    double? Contrast = null)
{
    public static readonly CaptureSettings Default = new();
}

/// <summary>Bộ điều khiển đèn nhiều kênh.</summary>
public interface ILightController : IDevice
{
    int ChannelCount { get; }

    /// <summary>Cường độ lớn nhất bộ điều khiển nhận — để kiểm bảng lệnh ngay lúc nạp.</summary>
    int MaxIntensity { get; }

    Task SetAsync(IReadOnlyList<LightChannelSetting> channels, CancellationToken ct);
}

/// <summary>Một kênh đèn và cường độ (0 = tắt; thang đo tuỳ bộ điều khiển, thường 0–255).</summary>
public sealed record LightChannelSetting(int Channel, int Intensity);

/// <summary>Sự kiện đổi trạng thái thiết bị — kèm lý do dạng thông điệp chưa dịch.</summary>
public sealed class DeviceStatusEventArgs : EventArgs
{
    public DeviceStatusEventArgs(string role, DeviceStatus status, Msg reason)
    {
        Role = role;
        Status = status;
        Reason = reason;
    }

    public string Role { get; }

    public DeviceStatus Status { get; }

    public Msg Reason { get; }
}
