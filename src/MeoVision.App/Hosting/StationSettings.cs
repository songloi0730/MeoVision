using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MeoVision.Core;
using MeoVision.Core.Localization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MeoVision.App.Hosting;

/// <summary>Cấu hình đã ghép hai lớp, kèm lỗi đọc file của trạm (nếu có).</summary>
public sealed record LoadedSettings(AppSettings Settings, AppPaths Paths, string StationFile,
    Msg? Problem);

/// <summary>
/// Hai lớp cấu hình (DESIGN_LOG [27]). <c>appsettings.json</c> cạnh exe là MẶC ĐỊNH của bản phát
/// hành — đi theo chương trình, không ai sửa. <c>Data\settings.json</c> chỉ chứa những gì TRẠM NÀY
/// đổi khác mặc định (thư mục ảnh ở ổ D, ngưỡng đĩa...). Cài bản mới không đè thông số đã chỉnh
/// tại máy; bản mới thêm mục nào thì trạm tự có giá trị mặc định của mục đó — cùng mục đích với
/// việc "nạp thêm tham số từ tài nguyên của chương trình" mà RefVis-D phải thêm ở bản 3.7.
/// </summary>
public static class StationSettings
{
    public const string FileName = "settings.json";
    public const string ProgramFile = "appsettings.json";
    private const string Section = "MeoVision";

    private static readonly JsonSerializerSettings ReadSettings = new()
    {
        // Mục lạ trong file trạm (do bản mới hơn ghi, hoặc gõ nhầm) không làm hỏng việc đọc.
        MissingMemberHandling = MissingMemberHandling.Ignore,
    };

    /// <summary>
    /// File trạm hỏng thì dùng mặc định và báo — KHÔNG ghi đè file hỏng: trong đó là thông số của
    /// máy, người sửa được nó còn cần đọc nó (RefVis-I ghi đè file hỏng bằng mặc định).
    /// </summary>
    public static LoadedSettings Load(string programDirectory)
    {
        Guard.ThrowIfNull(programDirectory, nameof(programDirectory));
        JObject defaults = ReadProgramFile(programDirectory);
        string dataRoot = defaults.ToObject<AppSettings>(JsonSerializer.Create(ReadSettings))!
            .DataRoot;
        string root = AppPaths.Resolve(dataRoot, programDirectory).Root;
        string stationFile = Path.Combine(root, FileName);

        JObject merged = (JObject)defaults.DeepClone();
        Msg? problem = null;
        if (File.Exists(stationFile))
        {
            try
            {
                JObject station = JObject.Parse(File.ReadAllText(stationFile, Encoding.UTF8));
                if (station[Section] is JObject overrides)
                {
                    overrides.Remove(nameof(AppSettings.DataRoot));   // file trạm nằm TRONG nó
                    merged.Merge(overrides, new JsonMergeSettings
                    {
                        MergeArrayHandling = MergeArrayHandling.Replace,
                        MergeNullValueHandling = MergeNullValueHandling.Ignore,
                    });
                }
            }
            catch (JsonException ex)
            {
                problem = Msg.Of("startup.settings.invalid", stationFile, ex.Message);
            }
        }

        AppSettings settings = merged.ToObject<AppSettings>(JsonSerializer.Create(ReadSettings))!;
        settings.DataRoot = dataRoot;
        AppPaths paths = AppPaths.Resolve(dataRoot, programDirectory, settings.ImagesRoot,
            settings.ResultsRoot, settings.LogsRoot);
        return new LoadedSettings(settings, paths, stationFile, problem);
    }

    /// <summary>Bản sao độc lập — form sửa trên bản sao, cấu hình đang chạy giữ nguyên tới lần mở lại.</summary>
    public static AppSettings Copy(AppSettings settings)
    {
        Guard.ThrowIfNull(settings, nameof(settings));
        return JObject.FromObject(settings).ToObject<AppSettings>(JsonSerializer.Create(ReadSettings))!;
    }

    /// <summary>Giá trị mặc định của bản phát hành (để nút "Về mặc định" và để so khác).</summary>
    public static AppSettings Defaults(string programDirectory)
    {
        Guard.ThrowIfNull(programDirectory, nameof(programDirectory));
        return ReadProgramFile(programDirectory).ToObject<AppSettings>(
            JsonSerializer.Create(ReadSettings))!;
    }

    /// <summary>
    /// Ghi những mục của <paramref name="settings"/> KHÁC mặc định vào file trạm. Bản cũ giữ lại
    /// thành <c>settings.json.bak</c> (RefVis-D, RefVis-H đều làm vậy); ghi qua file tạm.
    /// </summary>
    /// <returns>Các mục đổi so với file trạm trước đó: khoá → (cũ, mới), để ghi nhật ký.</returns>
    public static IReadOnlyDictionary<string, (string Old, string New)> Save(
        string programDirectory, string stationFile, AppSettings settings)
    {
        Guard.ThrowIfNull(programDirectory, nameof(programDirectory));
        Guard.ThrowIfNull(stationFile, nameof(stationFile));
        Guard.ThrowIfNull(settings, nameof(settings));
        JObject defaults = JObject.FromObject(Defaults(programDirectory));
        JObject wanted = JObject.FromObject(settings);
        wanted.Remove(nameof(AppSettings.DataRoot));
        JObject before = JObject.FromObject(Merged(programDirectory, stationFile));

        JObject overrides = Diff(defaults, wanted);
        JObject file = new() { [Section] = overrides };
        if (File.Exists(stationFile))
        {
            File.Copy(stationFile, stationFile + ".bak", overwrite: true);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(stationFile)!);
        string temp = stationFile + ".tmp";
        File.WriteAllText(temp, file.ToString(Formatting.Indented), new UTF8Encoding(false));
        if (File.Exists(stationFile))
        {
            File.Replace(temp, stationFile, null);
        }
        else
        {
            File.Move(temp, stationFile);
        }
        return Changes(before, wanted);
    }

    /// <summary>Các lá của <paramref name="wanted"/> khác <paramref name="baseline"/>, giữ cấu trúc.</summary>
    internal static JObject Diff(JObject baseline, JObject wanted)
    {
        JObject result = new();
        foreach (JProperty p in wanted.Properties())
        {
            JToken? old = Find(baseline, p.Name);
            if (p.Value is JObject child && old is JObject oldChild)
            {
                JObject inner = Diff(oldChild, child);
                if (inner.HasValues)
                {
                    result[p.Name] = inner;
                }
            }
            else if (old is null || !JToken.DeepEquals(old, p.Value))
            {
                result[p.Name] = p.Value.DeepClone();
            }
        }
        return result;
    }

    private static AppSettings Merged(string programDirectory, string stationFile)
    {
        JObject merged = ReadProgramFile(programDirectory);
        try
        {
            if (File.Exists(stationFile) &&
                JObject.Parse(File.ReadAllText(stationFile, Encoding.UTF8))[Section] is JObject o)
            {
                merged.Merge(o, new JsonMergeSettings { MergeArrayHandling = MergeArrayHandling.Replace });
            }
        }
        catch (JsonException)
        {
            // File hỏng: coi như toàn mặc định — bản .bak vẫn giữ nội dung hỏng để xem lại.
        }
        return merged.ToObject<AppSettings>(JsonSerializer.Create(ReadSettings))!;
    }

    private static Dictionary<string, (string Old, string New)> Changes(JObject before,
        JObject after)
    {
        Dictionary<string, (string, string)> changes = new(StringComparer.OrdinalIgnoreCase);
        foreach (JValue leaf in after.Descendants().OfType<JValue>())
        {
            string path = leaf.Path;
            string old = before.SelectToken(path)?.ToString(Formatting.None) ?? string.Empty;
            string now = leaf.ToString(Formatting.None);
            if (old != now)
            {
                changes[path] = (old, now);
            }
        }
        return changes;
    }

    private static JToken? Find(JObject o, string name) =>
        o.Properties().FirstOrDefault(p =>
            string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))?.Value;

    private static JObject ReadProgramFile(string programDirectory)
    {
        JObject file = JObject.Parse(File.ReadAllText(Path.Combine(programDirectory, ProgramFile),
            Encoding.UTF8));
        return file[Section] as JObject ?? new JObject();
    }
}
