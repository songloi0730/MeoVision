using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MeoVision.Core;
using MeoVision.Core.Devices;
using MeoVision.Core.Localization;

namespace MeoVision.Vision.Devices;

/// <summary>Loại "CognexGigE": <c>serialNumber</c> (bắt buộc), <c>videoFormat</c>, <c>timeoutMs</c>.</summary>
public sealed class CognexGigECameraFactory : IDeviceFactory
{
    public DeviceKind Kind => DeviceKind.Camera;

    public string Type => "CognexGigE";

    public IReadOnlyList<Msg> Validate(DeviceConfig config)
    {
        List<Msg> problems = new();
        Read(config, problems);
        return problems;
    }

    public IDevice Create(DeviceConfig config, string configDirectory) =>
        Read(config, new List<Msg>());

    private static CognexGigECamera Read(DeviceConfig config, List<Msg> problems)
    {
        SettingsReader s = new(config, problems);
        return new CognexGigECamera(config.Role, s.Required("serialNumber"),
            s.Optional("videoFormat", string.Empty), s.Double("timeoutMs", 2000, 50, 60000));
    }
}

/// <summary>
/// Loại "Folder": <c>folder</c> (bắt buộc; tương đối thì tính từ thư mục chứa machine.json),
/// <c>patterns</c>, <c>loop</c>, <c>grey</c>.
/// </summary>
public sealed class FolderCameraFactory : IDeviceFactory
{
    private const string DefaultPatterns = "*.bmp;*.png;*.jpg;*.tif;*.tiff;*.idb;*.cdb";

    public DeviceKind Kind => DeviceKind.Camera;

    public string Type => "Folder";

    public IReadOnlyList<Msg> Validate(DeviceConfig config)
    {
        List<Msg> problems = new();
        Read(config, string.Empty, problems);
        return problems;
    }

    public IDevice Create(DeviceConfig config, string configDirectory) =>
        Read(config, configDirectory, new List<Msg>());

    private static FolderCamera Read(DeviceConfig config, string configDirectory,
        List<Msg> problems)
    {
        SettingsReader s = new(config, problems);
        string folder = Environment.ExpandEnvironmentVariables(s.Required("folder"));
        if (folder.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            // Hay gặp nhất: đường dẫn viết trong JSON với MỘT dấu gạch chéo ngược — "\b"
            // thành backspace, "\n" thành xuống dòng. JSON vẫn hợp lệ, chỉ đường dẫn hỏng.
            problems.Add(Msg.Of("machine.setting.badPath", config.Role, "folder", folder));
            folder = string.Empty;
        }
        if (folder.Length > 0 && !Path.IsPathRooted(folder) && configDirectory.Length > 0)
        {
            folder = Path.GetFullPath(Path.Combine(configDirectory, folder));
        }
        List<string> patterns = s.Optional("patterns", DefaultPatterns)
            .Split(';').Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
        return new FolderCamera(config.Role, folder, patterns, s.Bool("loop", true),
            s.Bool("grey", true));
    }
}
