using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using MeoVision.App.Hosting;
using MeoVision.App.Products;
using MeoVision.Core;
using MeoVision.Core.Localization;
using Newtonsoft.Json;

namespace MeoVision.App.Maintenance;

/// <summary>Mục lục của một file sao lưu trạm (<c>station.json</c> trong zip).</summary>
public sealed class StationBackupInfo
{
    public string Machine { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public string AppVersion { get; set; } = string.Empty;

    /// <summary>Tên mục trong zip (<c>data/...</c>, <c>program/...</c>) → SHA-256.</summary>
    public SortedDictionary<string, string> Files { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>Kết quả khôi phục. Có lỗi thì KHÔNG file nào bị ghi.</summary>
public sealed record StationRestoreResult(bool Ok, IReadOnlyList<Msg> Problems,
    StationBackupInfo? Info, string? MovedAsideTo);

/// <summary>
/// Sao lưu / khôi phục CẢ TRẠM (Chương 24, mục 24.3): một file zip chứa mọi thứ cần để dựng lại
/// trạm trên một máy tính mới — cấu hình máy, hiệu chuẩn, sản phẩm, tài khoản, appsettings — kèm mã
/// băm từng file. KHÔNG chứa ảnh, kết quả, log: đó là dữ liệu sản xuất, không cần để trạm chạy.
/// </summary>
public sealed class StationBackup
{
    public const string ManifestEntry = "station.json";
    public const string RestoredSettingsFile = "restored-appsettings.json";

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

    private readonly AppPaths _paths;
    private readonly string _programDirectory;

    public StationBackup(AppPaths paths, string programDirectory)
    {
        Guard.ThrowIfNull(paths, nameof(paths));
        Guard.ThrowIfNull(programDirectory, nameof(programDirectory));
        _paths = paths;
        _programDirectory = programDirectory;
    }

    /// <summary>Những gì vào bản sao lưu: tên mục trong zip → file trên đĩa.</summary>
    public IReadOnlyList<(string Entry, string File)> Contents()
    {
        List<(string, string)> list = new();
        void AddFile(string file)
        {
            if (File.Exists(file))
            {
                list.Add(("data/" + ProductBackup.Relative(_paths.Root, file), file));
            }
        }
        void AddFolder(string folder)
        {
            if (Directory.Exists(folder))
            {
                foreach (string file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
                             .Where(f => !f.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                             .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                {
                    AddFile(file);
                }
            }
        }
        AddFile(_paths.MachineFile);
        AddFolder(_paths.MachineCalibration);
        AddFolder(_paths.Products);
        AddFile(_paths.ActiveProductFile);
        AddFile(_paths.UsersFile);
        // DESIGN_LOG [27]: thông số riêng của trạm (thư mục ảnh/log, ngưỡng) — khôi phục là dùng ngay.
        AddFile(Path.Combine(_paths.Root, Hosting.StationSettings.FileName));
        string settings = Path.Combine(_programDirectory, "appsettings.json");
        if (File.Exists(settings))
        {
            list.Add(("program/appsettings.json", settings));
        }
        return list;
    }

    public StationBackupInfo Create(string zipFile, string user, string appVersion)
    {
        Guard.ThrowIfNull(zipFile, nameof(zipFile));
        StationBackupInfo info = new()
        {
            Machine = Environment.MachineName, CreatedAt = DateTime.Now, CreatedBy = user,
            AppVersion = appVersion,
        };
        string temp = zipFile + ".tmp";
        using (ZipArchive zip = ZipFile.Open(temp, ZipArchiveMode.Create))
        {
            foreach ((string entry, string file) in Contents())
            {
                info.Files[entry] = ProductBackup.Sha256Of(file);
                zip.CreateEntryFromFile(file, entry);
            }
            using StreamWriter manifest = new(zip.CreateEntry(ManifestEntry).Open(),
                new UTF8Encoding(false));
            manifest.Write(JsonConvert.SerializeObject(info, JsonSettings));
        }
        if (File.Exists(zipFile))
        {
            File.Delete(zipFile);
        }
        File.Move(temp, zipFile);
        return info;
    }

    /// <summary>
    /// Kiểm TOÀN BỘ trước (mục lục đọc được, mọi file có mặt và đúng mã băm, không đường dẫn ra
    /// ngoài thư mục dữ liệu), rồi mới chuyển cấu hình đang có sang <c>backup\</c> và giải nén.
    /// Chương trình phải đang KHÔNG chạy — người gọi giữ mutex một bản chạy.
    /// </summary>
    public StationRestoreResult Restore(string zipFile)
    {
        Guard.ThrowIfNull(zipFile, nameof(zipFile));
        using ZipArchive zip = ZipFile.OpenRead(zipFile);
        ZipArchiveEntry? manifestEntry = zip.GetEntry(ManifestEntry);
        if (manifestEntry is null)
        {
            return Fail(Msg.Of("station.restore.noManifest", ManifestEntry));
        }
        StationBackupInfo info;
        using (StreamReader reader = new(manifestEntry.Open(), Encoding.UTF8))
        {
            info = JsonConvert.DeserializeObject<StationBackupInfo>(reader.ReadToEnd(),
                JsonSettings) ?? new StationBackupInfo();
        }
        List<Msg> problems = new();
        foreach (KeyValuePair<string, string> f in info.Files)
        {
            if (Target(f.Key) is null)
            {
                problems.Add(Msg.Of("station.restore.badPath", f.Key));
                continue;
            }
            ZipArchiveEntry? entry = zip.GetEntry(f.Key);
            if (entry is null)
            {
                problems.Add(Msg.Of("station.restore.missing", f.Key));
                continue;
            }
            if (Sha256(entry) != f.Value)
            {
                problems.Add(Msg.Of("station.restore.changed", f.Key));
            }
        }
        if (!info.Files.Keys.Any(k => k == "data/machine.json"))
        {
            problems.Add(Msg.Of("station.restore.noMachine"));
        }
        if (problems.Count > 0)
        {
            return new StationRestoreResult(false, problems, info, null);
        }

        string? aside = MoveAside();
        // Lượt hai: giải nén. Đọc lại từ zip thay vì giữ trong RAM — thư mục sản phẩm có bộ ảnh thử
        // có thể nặng vài GB.
        foreach (string name in info.Files.Keys)
        {
            string target = Target(name)!;
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            zip.GetEntry(name)!.ExtractToFile(target, overwrite: true);
        }
        return new StationRestoreResult(true, problems, info, aside);
    }

    /// <summary>
    /// Mục <c>data/...</c> → file trong thư mục dữ liệu; <c>program/appsettings.json</c> → đặt cạnh
    /// dữ liệu dưới tên khác (thư mục chương trình thường chỉ Administrators ghi được, và máy mới
    /// có thể đặt dữ liệu ở ổ khác — người cài so rồi tự chép). Null = đường dẫn không hợp lệ (zip
    /// có "../" — "zip slip").
    /// </summary>
    private string? Target(string entry)
    {
        if (entry == "program/appsettings.json")
        {
            return Path.Combine(_paths.Root, RestoredSettingsFile);
        }
        if (!entry.StartsWith("data/", StringComparison.Ordinal))
        {
            return null;
        }
        string root = Path.GetFullPath(_paths.Root).TrimEnd('\\') + "\\";
        string full = Path.GetFullPath(Path.Combine(root,
            entry.Substring("data/".Length).Replace('/', '\\')));
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full : null;
    }

    /// <summary>
    /// Cấu hình đang có không bị xoá — chuyển nguyên sang một thư mục sao lưu. Máy mới (thư mục dữ
    /// liệu chỉ có các thư mục rỗng) thì không có gì để chuyển: trả null.
    /// </summary>
    private string? MoveAside()
    {
        string[] files = new[]
        {
            _paths.MachineFile, _paths.ActiveProductFile, _paths.UsersFile,
            Path.Combine(_paths.Root, RestoredSettingsFile),
        }.Where(File.Exists).ToArray();
        string[] folders = new[] { _paths.MachineCalibration, _paths.Products }
            .Where(d => Directory.Exists(d) &&
                        Directory.EnumerateFiles(d, "*", SearchOption.AllDirectories).Any())
            .ToArray();
        if (files.Length == 0 && folders.Length == 0)
        {
            return null;
        }
        string aside = Path.Combine(_paths.Backup, "station-before-restore-" +
            DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(aside);
        foreach (string file in files)
        {
            File.Move(file, Path.Combine(aside, Path.GetFileName(file)));
        }
        foreach (string folder in folders)
        {
            string target = Path.Combine(aside, ProductBackup.Relative(_paths.Root, folder)
                .Replace('/', '\\'));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            Directory.Move(folder, target);
        }
        return aside;
    }

    private static StationRestoreResult Fail(Msg problem) =>
        new(false, new[] { problem }, null, null);

    private static string Sha256(ZipArchiveEntry entry)
    {
        using SHA256 sha = SHA256.Create();
        using Stream stream = entry.Open();
        return string.Concat(sha.ComputeHash(stream).Select(b => b.ToString("x2",
            CultureInfo.InvariantCulture)));
    }
}
