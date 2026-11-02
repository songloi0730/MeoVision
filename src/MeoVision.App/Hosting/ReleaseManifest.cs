using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MeoVision.App.Products;
using MeoVision.Core;
using MeoVision.Core.Localization;
using Newtonsoft.Json;

namespace MeoVision.App.Hosting;

/// <summary>
/// <c>release.json</c> cạnh file exe (Chương 24, mục 24.2): bản phát hành này là bản nào, build lúc
/// nào, trên bản VisionPro nào, và mã băm của từng file. Do chính exe ghi (<c>--write-release</c>)
/// để chỉ có MỘT chỗ tính mã băm.
/// </summary>
public sealed class ReleaseInfo
{
    public string Version { get; set; } = string.Empty;

    public DateTime BuiltAt { get; set; }

    /// <summary>
    /// Bản VisionPro trên máy build, đúng chuỗi <c>CogMisc.VisionProReleaseNumber()</c>.
    /// </summary>
    public string VisionPro { get; set; } = string.Empty;

    /// <summary>Đường dẫn tương đối (dấu /) → SHA-256.</summary>
    public SortedDictionary<string, string> Files { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Đọc, ghi và kiểm <c>release.json</c>.</summary>
public static class ReleaseManifest
{
    public const string FileName = "release.json";

    /// <summary>
    /// Không băm: chính manifest, và <c>appsettings.json</c> — file mỗi máy được sửa (thư mục dữ
    /// liệu, ngưỡng đĩa…), sửa nó không phải là cài hỏng.
    /// </summary>
    private static readonly string[] Unhashed = { FileName, "appsettings.json" };

    private static readonly JsonSerializerSettings JsonSettings = new()
    {
        Formatting = Formatting.Indented,
        // camelCase cho tên thuộc tính, KHÔNG cho khoá dictionary: đo thật, resolver camelCase mặc
        // định ghi "MeoVision.App.dll" thành "meoVision.App.dll".
        ContractResolver = new Newtonsoft.Json.Serialization.DefaultContractResolver
        {
            NamingStrategy = new Newtonsoft.Json.Serialization.CamelCaseNamingStrategy
            {
                ProcessDictionaryKeys = false,
            },
        },
    };

    public static ReleaseInfo Write(string programDirectory, string version, string visionPro)
    {
        Guard.ThrowIfNull(programDirectory, nameof(programDirectory));
        ReleaseInfo info = new()
        {
            Version = version, BuiltAt = DateTime.Now, VisionPro = visionPro,
        };
        foreach (string file in Hashed(programDirectory))
        {
            info.Files[ProductBackup.Relative(programDirectory, file)] =
                ProductBackup.Sha256Of(file);
        }
        File.WriteAllText(Path.Combine(programDirectory, FileName),
            JsonConvert.SerializeObject(info, JsonSettings), new UTF8Encoding(false));
        return info;
    }

    /// <summary>Null = không có release.json (chạy từ thư mục build trên máy dev).</summary>
    public static ReleaseInfo? Read(string programDirectory)
    {
        string file = Path.Combine(programDirectory, FileName);
        return File.Exists(file)
            ? JsonConvert.DeserializeObject<ReleaseInfo>(File.ReadAllText(file), JsonSettings)
            : null;
    }

    /// <summary>
    /// Thiếu file, file khác mã băm, DLL/EXE lạ không có trong bản phát hành — ba kiểu cài hỏng gặp
    /// ngoài hiện trường: chép thiếu, chép đè một DLL "sửa nóng", chép lẫn hai bản.
    /// </summary>
    public static IReadOnlyList<Msg> Verify(string programDirectory, ReleaseInfo release)
    {
        Guard.ThrowIfNull(programDirectory, nameof(programDirectory));
        Guard.ThrowIfNull(release, nameof(release));
        List<Msg> problems = new();
        foreach (KeyValuePair<string, string> f in release.Files)
        {
            string path = Path.Combine(programDirectory, f.Key.Replace('/', '\\'));
            if (!File.Exists(path))
            {
                problems.Add(Msg.Of("release.missing", f.Key));
            }
            else if (!string.Equals(ProductBackup.Sha256Of(path), f.Value,
                         StringComparison.OrdinalIgnoreCase))
            {
                problems.Add(Msg.Of("release.changed", f.Key));
            }
        }
        foreach (string file in Hashed(programDirectory))
        {
            string relative = ProductBackup.Relative(programDirectory, file);
            string extension = Path.GetExtension(file);
            if (!release.Files.ContainsKey(relative) &&
                (extension.Equals(".dll", StringComparison.OrdinalIgnoreCase) ||
                 extension.Equals(".exe", StringComparison.OrdinalIgnoreCase)))
            {
                problems.Add(Msg.Of("release.extra", relative));
            }
        }
        return problems;
    }

    /// <summary>
    /// Thư mục dữ liệu mặc định nằm TRONG thư mục chương trình (DESIGN_LOG [26]) — không băm: nó
    /// đổi liên tục và có thể chứa hàng GB ảnh.
    /// </summary>
    public const string DataFolder = "Data";

    private static IEnumerable<string> Hashed(string programDirectory)
    {
        string data = Path.Combine(programDirectory.TrimEnd('\\'), DataFolder) + "\\";
        return Directory.EnumerateFiles(programDirectory, "*", SearchOption.TopDirectoryOnly)
            .Concat(Directory.EnumerateDirectories(programDirectory)
                .Where(d => !(d + "\\").Equals(data, StringComparison.OrdinalIgnoreCase))
                .SelectMany(d => Directory.EnumerateFiles(d, "*", SearchOption.AllDirectories)))
            .Where(f => !Unhashed.Contains(Path.GetFileName(f), StringComparer.OrdinalIgnoreCase) ||
                        !string.Equals(Path.GetDirectoryName(f)?.TrimEnd('\\'),
                            programDirectory.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);
    }
}
