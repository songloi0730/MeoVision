using System;
using System.Collections.Generic;
using System.Globalization;
using MeoVision.Core.Localization;

namespace MeoVision.Core.Devices;

public enum DeviceKind
{
    Camera,
    Light,
}

/// <summary>
/// Tạo thiết bị cho MỘT loại (<see cref="Type"/>, ví dụ "CognexGigE", "Folder", "AsciiLight").
/// Thêm hãng camera/đèn mới = thêm một factory, đăng ký ở composition root (Chương 18, mục 18.2).
/// </summary>
public interface IDeviceFactory
{
    DeviceKind Kind { get; }

    string Type { get; }

    /// <summary>
    /// Kiểm thiết lập lúc NẠP <c>machine.json</c> — trước khi đụng tới phần cứng.
    /// </summary>
    IReadOnlyList<Msg> Validate(DeviceConfig config);

    /// <summary>Tạo thiết bị (chưa mở). Chỉ gọi khi <see cref="Validate"/> không báo lỗi.</summary>
    IDevice Create(DeviceConfig config, string configDirectory);
}

/// <summary>Lỗi thiết bị mang thông điệp chưa dịch — UI và log tự dịch theo ngôn ngữ của mình.</summary>
public sealed class DeviceException : Exception
{
    public DeviceException(Msg message, Exception? inner = null, bool transient = false)
        : base(message?.ToString(), inner)
    {
        Guard.ThrowIfNull(message, nameof(message));
        Info = message;
        Transient = transient;
    }

    public Msg Info { get; }

    /// <summary>
    /// Lỗi lẻ của MỘT lần thao tác, thiết bị vẫn dùng được — ví dụ một khung ảnh hụt gói tin
    /// (Chương 20, mục 20.4). Không đánh dấu thiết bị hỏng; lệnh được phép chụp lại.
    /// </summary>
    public bool Transient { get; }
}

/// <summary>
/// Đọc thiết lập dạng chuỗi của một thiết bị, gom MỌI lỗi vào một danh sách thay vì ném ở lỗi
/// đầu tiên — người sửa <c>machine.json</c> thấy hết chỗ sai trong một lần.
/// </summary>
public sealed class SettingsReader
{
    private readonly DeviceConfig _config;
    private readonly List<Msg> _problems;

    public SettingsReader(DeviceConfig config, List<Msg> problems)
    {
        Guard.ThrowIfNull(config, nameof(config));
        Guard.ThrowIfNull(problems, nameof(problems));
        _config = config;
        _problems = problems;
    }

    public string Required(string key)
    {
        if (_config.Settings.TryGetValue(key, out string? value) && value.Trim().Length > 0)
        {
            return value.Trim();
        }
        _problems.Add(Msg.Of("machine.setting.missing", _config.Role, key));
        return string.Empty;
    }

    public string Optional(string key, string defaultValue) =>
        _config.Settings.TryGetValue(key, out string? value) && value.Trim().Length > 0
            ? value.Trim()
            : defaultValue;

    public bool Bool(string key, bool defaultValue)
    {
        string text = Optional(key, string.Empty);
        if (text.Length == 0)
        {
            return defaultValue;
        }
        if (bool.TryParse(text, out bool value))
        {
            return value;
        }
        _problems.Add(Msg.Of("machine.setting.badValue", _config.Role, key, text, "true, false"));
        return defaultValue;
    }

    public int Int(string key, int defaultValue, int min, int max) =>
        (int)Number(key, defaultValue, min, max, integer: true);

    public double Double(string key, double defaultValue, double min, double max) =>
        Number(key, defaultValue, min, max, integer: false);

    private double Number(string key, double defaultValue, double min, double max, bool integer)
    {
        if (!_config.Settings.TryGetValue(key, out string? text) || text.Trim().Length == 0)
        {
            return defaultValue;
        }

        // Số trong file cấu hình luôn viết kiểu bất biến (dấu chấm), không theo ngôn ngữ máy.
        NumberStyles style = integer ? NumberStyles.Integer : NumberStyles.Float;
        if (!double.TryParse(text.Trim(), style, CultureInfo.InvariantCulture, out double value))
        {
            _problems.Add(Msg.Of("machine.setting.notNumber", _config.Role, key, text));
            return defaultValue;
        }
        if (value < min || value > max)
        {
            _problems.Add(Msg.Of("machine.setting.outOfRange", _config.Role, key, text, min, max));
            return defaultValue;
        }
        return value;
    }
}
