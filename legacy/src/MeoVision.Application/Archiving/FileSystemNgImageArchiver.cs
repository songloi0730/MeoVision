using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MeoVision.Application.Alarms;
using MeoVision.Core;
using MeoVision.Core.Contracts;
using Microsoft.Extensions.Logging;

namespace MeoVision.Application.Archiving;

public sealed class FileSystemNgImageArchiver : INgImageArchiver
{
    private readonly string _directory;
    private readonly int _retentionDays;
    private readonly long _maxBytes;
    private readonly IAlarmService _alarmService;
    private readonly ILogger<FileSystemNgImageArchiver> _logger;

    public FileSystemNgImageArchiver(
        MeoVisionHostOptions options,
        IAlarmService alarmService,
        ILogger<FileSystemNgImageArchiver> logger)
    {
        Guard.ThrowIfNull(options, nameof(options));
        Guard.ThrowIfNull(alarmService, nameof(alarmService));
        Guard.ThrowIfNull(logger, nameof(logger));

        _directory = Path.IsPathRooted(options.NgImageDirectory)
            ? options.NgImageDirectory
            : Path.Combine(AppContext.BaseDirectory, options.NgImageDirectory);
        _retentionDays = options.NgImageRetentionDays;
        _maxBytes = options.NgImageMaxBytes;
        _alarmService = alarmService;
        _logger = logger;
    }

    // Quyết định OUTCOME nào cần lưu ảnh (chỉ NG/Warning, chính sách Ch14 §14.5.1) thuộc về
    // InspectionCycleService (policy) — lớp này chỉ lo CÁCH lưu + retention (cơ chế), luôn lưu
    // khi được gọi.
    public async Task<string?> ArchiveIfNeededAsync(
        Bitmap image, VisionOutcome outcome, string correlationId, CancellationToken ct = default)
    {
        Guard.ThrowIfNull(image, nameof(image));
        Guard.ThrowIfNull(correlationId, nameof(correlationId));

        Directory.CreateDirectory(_directory);
        await EnforceRetentionAsync(ct).ConfigureAwait(false);

        string fileName = $"{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}_{correlationId}_{outcome}.png";
        string path = Path.Combine(_directory, fileName);

        await Task.Run(() => image.Save(path, ImageFormat.Png), ct).ConfigureAwait(false);
        _logger.LogInformation("Đã lưu ảnh NG/Warning: {Path}", path);
        return path;
    }

    private async Task EnforceRetentionAsync(CancellationToken ct)
    {
        DateTime cutoff = DateTime.UtcNow.AddDays(-_retentionDays);
        foreach (FileInfo file in new DirectoryInfo(_directory).GetFiles("*.png")
            .Where(f => f.CreationTimeUtc < cutoff))
        {
            file.Delete();
        }

        FileInfo[] remaining = new DirectoryInfo(_directory).GetFiles("*.png");
        long totalBytes = remaining.Sum(f => f.Length);

        if (totalBytes > _maxBytes)
        {
            foreach (FileInfo file in remaining.OrderBy(f => f.CreationTimeUtc))
            {
                if (totalBytes <= _maxBytes)
                {
                    break;
                }

                totalBytes -= file.Length;
                file.Delete();
            }
        }

        // Cảnh báo TRƯỚC khi đầy đĩa (còn 10% dung lượng) — Ch14 §14.5.2: tốt hơn để vận hành
        // viên biết sớm thay vì phát hiện lúc archive thất bại vì hết chỗ.
        if (totalBytes > _maxBytes * 0.9)
        {
            await _alarmService.RaiseAsync(
                AlarmCodes.NgImageStorageNearFull,
                $"Thư mục lưu ảnh NG gần đầy: {totalBytes:N0}/{_maxBytes:N0} bytes",
                AlarmSeverity.Warning, ct).ConfigureAwait(false);
        }
    }
}
