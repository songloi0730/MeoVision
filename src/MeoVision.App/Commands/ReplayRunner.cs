using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using MeoVision.App.Products;
using MeoVision.App.Results;
using MeoVision.Core;
using MeoVision.Core.Commands;
using MeoVision.Core.Devices;
using MeoVision.Core.Storage;

namespace MeoVision.App.Commands;

/// <summary>Một ảnh mà bản nháp kết luận khác lúc máy chạy thật.</summary>
public sealed record ReplayDifference(ResultRow Recorded, CommandStatus Status,
    IReadOnlyList<OutputValue> Outputs);

/// <summary>
/// Kết quả chạy lại. <see cref="OtherVersion"/>: dòng có ảnh nhưng chạy bằng phiên bản khác bản
/// đang chạy — kết quả ghi lại của chúng không phải "bản đang chạy làm gì", nên không so.
/// </summary>
public sealed record ReplayReport(int Images, int Same, IReadOnlyList<ReplayDifference> Differences,
    int OtherVersion, int MissingImages, int Failed, long ElapsedMs)
{
    public static readonly ReplayReport Empty =
        new(0, 0, Array.Empty<ReplayDifference>(), 0, 0, 0, 0);
}

/// <summary>
/// Chạy lại ảnh đã lưu qua bản nháp (Chương 24, mục 24.4; Chương 16, mục 16.3.4). Ba quy tắc:
/// (1) chỉ ảnh của ĐÚNG phiên bản đang chạy — so với câu trả lời mà máy đã gửi đi; (2) chạy theo
/// ĐÚNG thứ tự thời gian, trên MỘT job — job có script giữ trạng thái chỉ lộ lỗi trong dòng chảy
/// liên tục; (3) "khác" không có nghĩa là "sai": kết quả cũ không phải ground truth, người xem từng
/// ảnh khác mới kết luận được.
/// </summary>
public sealed class ReplayRunner
{
    private readonly ResultReader _reader;
    private readonly IImageStore _images;

    public ReplayRunner(ResultReader reader, IImageStore images)
    {
        Guard.ThrowIfNull(reader, nameof(reader));
        Guard.ThrowIfNull(images, nameof(images));
        _reader = reader;
        _images = images;
    }

    /// <param name="maxImages">Giữ N ảnh GẦN NHẤT, vẫn chạy theo thứ tự cũ → mới.</param>
    public ReplayReport Run(DraftSession draft, string code, string version, DateTime from,
        DateTime to, int maxImages, IProgress<int>? progress = null,
        CancellationToken ct = default)
    {
        Guard.ThrowIfNull(draft, nameof(draft));
        Guard.ThrowIfNull(code, nameof(code));
        Guard.ThrowIfNull(version, nameof(version));
        Stopwatch watch = Stopwatch.StartNew();
        List<ResultRow> withImage = new();
        for (DateTime day = from.Date; day <= to.Date; day = day.AddDays(1))
        {
            withImage.AddRange(_reader.Read(day).Where(r =>
                r.ImageFile.Length > 0 &&
                string.Equals(r.Code, code, StringComparison.OrdinalIgnoreCase)));
        }
        int otherVersion = withImage.Count(r => r.Version != version);
        List<ResultRow> rows = withImage.Where(r => r.Version == version)
            .OrderBy(r => r.Time).ToList();
        rows = rows.Skip(Math.Max(0, rows.Count - maxImages)).ToList();

        int same = 0, missing = 0, failed = 0, done = 0;
        List<ReplayDifference> differences = new();
        foreach (ResultRow row in rows)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(++done);
            if (!File.Exists(row.ImageFile))
            {
                missing++;   // đã bị dọn (xoay vòng) hoặc bị chép đi
                continue;
            }
            CameraImage image = _images.Load(row.ImageFile);
            draft.Calibrate(code, image);   // ảnh đọc từ file không mang hiệu chuẩn (Ch7 §7.1.3)
            using DisplayFrame frame = new(image, null);
            CommandResult? result = draft.Run(code, frame, withRecord: false);
            result?.Frame?.Dispose();
            if (result is null)
            {
                failed++;   // bản nháp vừa đóng
                break;
            }
            if (Same(row, result))
            {
                same++;
            }
            else
            {
                differences.Add(new ReplayDifference(row, result.Status, result.Outputs));
            }
        }
        return new ReplayReport(rows.Count, same, differences, otherVersion, missing, failed,
            watch.ElapsedMilliseconds);
    }

    /// <summary>
    /// Giống = cùng trạng thái và cùng CHỮ của từng output — như chạy kèm (Ch22).
    /// </summary>
    private static bool Same(ResultRow recorded, CommandResult replayed)
    {
        if (!string.Equals(recorded.Status, replayed.Status.ToString(),
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        foreach ((string name, string value) in recorded.Values)
        {
            OutputValue? output = replayed.Outputs.FirstOrDefault(o => o.Name == name);
            if (output is null || output.Text != value)
            {
                return false;
            }
        }
        return true;
    }
}
