using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using MeoVision.App.Hosting;
using MeoVision.Core;

namespace MeoVision.App.Maintenance;

/// <summary>Phạm vi gói chẩn đoán — mặc định HẸP (Chương 16, mục 16.4.5).</summary>
public sealed class DiagnosticOptions
{
    /// <summary>Log, kết quả và ảnh của N ngày gần nhất, tính cả hôm nay.</summary>
    public int Days { get; set; } = 2;

    /// <summary>Số ảnh NG/lỗi gần nhất; 0 = không kèm ảnh.</summary>
    public int MaxImages { get; set; } = 20;

    /// <summary>false = chỉ sản phẩm đang chạy.</summary>
    public bool AllProducts { get; set; }
}

/// <summary>Gói đã xuất: file, dung lượng, số mục, thời gian.</summary>
public sealed record DiagnosticBundleResult(string File, long Bytes, int Entries, long ElapsedMs);

/// <summary>
/// Gói chẩn đoán (Chương 24, mục 24.5): MỘT thao tác gom đủ bối cảnh để người ở xa chẩn đoán —
/// ghi chú của người thấy sự cố, thông tin hệ thống, cấu hình máy, sản phẩm đang chạy, log, nhật
/// ký thay đổi, file sự cố, kết quả và ảnh NG gần nhất. Không bao giờ có <c>users.json</c>.
/// </summary>
public sealed class DiagnosticBundle
{
    private readonly AppPaths _paths;
    private readonly string _programDirectory;

    public DiagnosticBundle(AppPaths paths, string programDirectory)
    {
        Guard.ThrowIfNull(paths, nameof(paths));
        Guard.ThrowIfNull(programDirectory, nameof(programDirectory));
        _paths = paths;
        _programDirectory = programDirectory;
    }

    /// <param name="systemReport">Văn bản do chương trình dựng: phiên bản, VisionPro, thiết
    /// bị...</param>
    public DiagnosticBundleResult Export(string note, string systemReport, string? productId,
        DiagnosticOptions options, DateTime now)
    {
        Guard.ThrowIfNull(note, nameof(note));
        Guard.ThrowIfNull(systemReport, nameof(systemReport));
        Guard.ThrowIfNull(options, nameof(options));
        Stopwatch watch = Stopwatch.StartNew();
        Directory.CreateDirectory(_paths.Diagnostics);
        string file = Path.Combine(_paths.Diagnostics, "diag-" + Environment.MachineName + "-" +
            now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".zip");
        string temp = file + ".tmp";
        int entries = 0;
        using (ZipArchive zip = ZipFile.Open(temp, ZipArchiveMode.Create))
        {
            void Text(string name, string content)
            {
                using StreamWriter w = new(zip.CreateEntry(name).Open(), new UTF8Encoding(true));
                w.Write(content);
                entries++;
            }
            void Add(string path, string name)
            {
                if (AddShared(zip, path, name))
                {
                    entries++;
                }
            }

            Text("note.txt", note);
            Text("system.txt", systemReport);
            Add(_paths.MachineFile, "machine.json");
            Add(Path.Combine(_programDirectory, "appsettings.json"), "appsettings.json");
            Add(Path.Combine(_programDirectory, ReleaseManifest.FileName),
                ReleaseManifest.FileName);
            foreach (string product in Products(productId, options.AllProducts))
            {
                foreach (string f in Directory.GetFiles(product, "*", SearchOption.AllDirectories))
                {
                    Add(f, "products/" + Path.GetFileName(product) + "/" +
                           f.Substring(product.Length + 1).Replace('\\', '/'));
                }
            }
            DateTime first = now.Date.AddDays(1 - Math.Max(1, options.Days));
            foreach (string log in Directory.Exists(_paths.Logs)
                         ? Directory.GetFiles(_paths.Logs, "*.log") : Array.Empty<string>())
            {
                if (File.GetLastWriteTime(log) >= first)
                {
                    Add(log, "logs/" + Path.GetFileName(log));
                }
            }
            foreach (string audit in Directory.Exists(_paths.Audit)
                         ? Directory.GetFiles(_paths.Audit, now.ToString("yyyy-MM",
                             CultureInfo.InvariantCulture) + "*.jsonl") : Array.Empty<string>())
            {
                Add(audit, "audit/" + Path.GetFileName(audit));
            }
            // File sự cố của CrashReporter (Ch17) — trong cùng thư mục, trừ chính các gói.
            foreach (string crash in Directory.GetFiles(_paths.Diagnostics, "crash_*.txt")
                         .Where(f => File.GetLastWriteTime(f) >= first))
            {
                Add(crash, "crash/" + Path.GetFileName(crash));
            }
            foreach (string day in Days(first, now))
            {
                string results = Path.Combine(_paths.Results, day);
                if (Directory.Exists(results))
                {
                    foreach (string csv in Directory.GetFiles(results, "*.csv"))
                    {
                        Add(csv, "results/" + day + "/" + Path.GetFileName(csv));
                    }
                }
            }
            foreach (string image in RecentImages(first, now, options.MaxImages))
            {
                Add(image, "images/" + image.Substring(_paths.Images.Length + 1)
                    .Replace('\\', '/'));
            }
        }
        File.Move(temp, file);
        return new DiagnosticBundleResult(file, new FileInfo(file).Length, entries,
            watch.ElapsedMilliseconds);
    }

    private IEnumerable<string> Products(string? productId, bool all)
    {
        if (!Directory.Exists(_paths.Products))
        {
            return Array.Empty<string>();
        }
        return Directory.GetDirectories(_paths.Products).Where(d => all ||
            string.Equals(Path.GetFileName(d), productId, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> Days(DateTime first, DateTime now)
    {
        for (DateTime d = first; d <= now.Date; d = d.AddDays(1))
        {
            yield return d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>Ảnh NG/lỗi mới nhất (tên file mang trạng thái — Ch23 §23.4.2).</summary>
    private IEnumerable<string> RecentImages(DateTime first, DateTime now, int max)
    {
        if (max <= 0)
        {
            return Array.Empty<string>();
        }
        return Days(first, now)
            .Select(d => Path.Combine(_paths.Images, d))
            .Where(Directory.Exists)
            .SelectMany(d => Directory.GetFiles(d, "*", SearchOption.AllDirectories))
            .Where(f => f.EndsWith("_NG.bmp", StringComparison.OrdinalIgnoreCase) ||
                        f.EndsWith("_ERROR.bmp", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(File.GetLastWriteTime)
            .Take(max);
    }

    /// <summary>
    /// Đọc chung với người đang ghi: log hôm nay đang được Serilog giữ mở — mở kiểu mặc định thì
    /// lỗi "being used by another process" (như form Lịch sử ở Ch23).
    /// </summary>
    private static bool AddShared(ZipArchive zip, string path, string name)
    {
        if (!File.Exists(path))
        {
            return false;
        }
        using FileStream source = new(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using Stream target = zip.CreateEntry(name, CompressionLevel.Fastest).Open();
        source.CopyTo(target);
        return true;
    }
}
