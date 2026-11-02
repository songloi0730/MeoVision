using System.Collections.Generic;

namespace MeoVision.Core.Devices;

/// <summary>
/// Nội dung <c>machine.json</c> — phần cứng của MÁY NÀY (Chương 18, mục 18.2). Không chứa gì của
/// sản phẩm: sản phẩm chỉ nhắc tới <see cref="DeviceConfig.Role"/>.
/// </summary>
public sealed class MachineConfig
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; }

    /// <summary>Tên máy hiển thị và ghi vào log/kết quả, ví dụ "AOI-02".</summary>
    public string MachineName { get; set; } = string.Empty;

    public List<DeviceConfig> Cameras { get; set; } = new();

    public List<DeviceConfig> Lights { get; set; } = new();

    /// <summary>
    /// Kênh nhận lệnh của máy (Chương 19, mục 19.2): mỗi kênh là một cổng TCP hoặc một vùng bắt
    /// tay PLC. Sản phẩm chỉ nhắc tới <see cref="ChannelConfig.Id"/>, như với vai trò thiết bị.
    /// </summary>
    public List<ChannelConfig> Channels { get; set; } = new();
}

/// <summary>
/// Một kênh nhận lệnh. <see cref="Type"/> và <see cref="Settings"/> do lớp giao tiếp (Chương 21)
/// đọc; Chương 19 chỉ cần <see cref="Id"/> để kiểm bảng lệnh của sản phẩm.
/// </summary>
public sealed class ChannelConfig
{
    public string Id { get; set; } = string.Empty;

    /// <summary>Tên hiển thị, ví dụ "Upper station".</summary>
    public string Name { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public Dictionary<string, string> Settings { get; set; } = new();
}

/// <summary>
/// Một thiết bị: vai trò (tên logic sản phẩm dùng), loại (chọn lớp cài đặt) và các thiết lập
/// riêng của loại đó dưới dạng chuỗi — mỗi loại tự kiểm thiết lập của mình.
/// </summary>
public sealed class DeviceConfig
{
    public string Role { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public Dictionary<string, string> Settings { get; set; } = new();
}
