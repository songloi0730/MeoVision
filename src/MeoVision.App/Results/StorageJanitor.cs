using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using MeoVision.Core;
using Microsoft.Extensions.Logging;

namespace MeoVision.App.Results;

/// <summary>Giữ bao lâu (Chương 23, mục 23.4.4).</summary>
public sealed class RetentionOptions
{
    public int KeepImageDays { get; set; } = 30;

    public int KeepResultDays { get; set; } = 365;

    /// <summary>Dưới mức này thì xoá thêm thư mục ảnh cũ nhất, kể cả khi chưa tới hạn.</summary>
    public double ImageMinFreeGb { get; set; } = 30;
}

/// <summary>Kết quả một lần dọn.</summary>
public sealed record CleanupReport(IReadOnlyList<string> DeletedFolders, long FreedBytes,
    double FreeGbAfter);

/// <summary>
/// Dọn ảnh và kết quả cũ (Chương 23, mục 23.4.4) — theo NGÀY (thư mục <c>yyyy-MM-dd</c>), hai luật:
/// quá hạn thì xoá; đĩa sắp đầy thì xoá thêm ngày ảnh cũ nhất. Không bao giờ đụng ngày hôm nay, và
/// kết quả (vài MB mỗi ngày) giữ lâu hơn ảnh (vài GB mỗi ngày): ảnh đã mất vẫn còn số để truy vết.
/// </summary>
public sealed class StorageJanitor
{
    private readonly string _imagesRoot;
    private readonly string _resultsRoot;
    private readonly RetentionOptions _options;
    private readonly Func<double> _freeGb;
    private readonly ILogger<StorageJanitor> _logger;

    /// <param name="freeGb">Dung lượng trống của ổ chứa ảnh, GB — truyền vào để test được.</param>
    public StorageJanitor(string imagesRoot, string resultsRoot, RetentionOptions options,
        Func<double> freeGb, ILogger<StorageJanitor> logger)
    {
        Guard.ThrowIfNull(imagesRoot, nameof(imagesRoot));
        Guard.ThrowIfNull(resultsRoot, nameof(resultsRoot));
        Guard.ThrowIfNull(options, nameof(options));
        Guard.ThrowIfNull(freeGb, nameof(freeGb));
        Guard.ThrowIfNull(logger, nameof(logger));
        _imagesRoot = imagesRoot;
        _resultsRoot = resultsRoot;
        _options = options;
        _freeGb = freeGb;
        _logger = logger;
    }

    public static Func<double> FreeGbOf(string folder) => () =>
        new DriveInfo(Path.GetPathRoot(Path.GetFullPath(folder))!).AvailableFreeSpace / 1e9;

    public CleanupReport Run(DateTime today)
    {
        List<string> deleted = new();
        long freed = 0;
        DateTime day0 = today.Date;
        foreach ((string folder, DateTime day) in Days(_imagesRoot))
        {
            if (day < day0.AddDays(-_options.KeepImageDays))
            {
                freed += Delete(folder, deleted);
            }
        }
        foreach ((string folder, DateTime day) in Days(_resultsRoot))
        {
            if (day < day0.AddDays(-_options.KeepResultDays))
            {
                freed += Delete(folder, deleted);
            }
        }
        // Luật dung lượng: ảnh cũ nhất trước, chỉ ảnh, không bao giờ ngày hôm nay.
        foreach ((string folder, DateTime day) in Days(_imagesRoot).OrderBy(d => d.Day))
        {
            if (_freeGb() >= _options.ImageMinFreeGb || day >= day0)
            {
                break;
            }
            freed += Delete(folder, deleted);
        }
        CleanupReport report = new(deleted, freed, _freeGb());
        if (deleted.Count > 0)
        {
            _logger.LogInformation("Storage cleanup: deleted {Count} day folder(s), {Mb} MB, free " +
                                   "{Free:0.0} GB: {Folders}", deleted.Count, freed / 1_000_000,
                report.FreeGbAfter, string.Join(", ", deleted));
        }
        return report;
    }

    private static IEnumerable<(string Folder, DateTime Day)> Days(string root)
    {
        if (!Directory.Exists(root))
        {
            yield break;
        }
        foreach (string folder in Directory.GetDirectories(root))
        {
            // Chỉ thư mục đúng dạng ngày — thư mục khác (ai đó chép vào) không bao giờ bị xoá.
            if (DateTime.TryParseExact(Path.GetFileName(folder), "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime day))
            {
                yield return (folder, day);
            }
        }
    }

    private long Delete(string folder, List<string> deleted)
    {
        long size = 0;
        try
        {
            size = Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
                .Sum(f => new FileInfo(f).Length);
            Directory.Delete(folder, recursive: true);
            deleted.Add(Path.GetFileName(Path.GetDirectoryName(folder)!) + "/" +
                        Path.GetFileName(folder));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // File đang mở (ai đó đang xem ảnh): để lần dọn sau.
            _logger.LogWarning(ex, "Could not delete {Folder}", folder);
            return 0;
        }
        return size;
    }
}
