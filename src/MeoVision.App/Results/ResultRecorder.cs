using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using MeoVision.App.Commands;
using MeoVision.Core;
using MeoVision.Core.Commands;
using MeoVision.Core.Storage;
using Microsoft.Extensions.Logging;

namespace MeoVision.App.Results;

/// <summary>Lưu gì (Chương 23, mục 23.4). Đặt trong appsettings.json — cấu hình của MÁY.</summary>
public sealed class ResultRecorderOptions
{
    /// <summary>Lưu ảnh của lệnh NG và lệnh lỗi có ảnh.</summary>
    public bool SaveNgImages { get; set; } = true;

    /// <summary>Lưu một ảnh OK sau mỗi N lệnh OK của cùng mã lệnh; 0 = không lưu ảnh OK.</summary>
    public int SaveOkImagesEvery { get; set; }

    /// <summary>Số ảnh chờ ghi tối đa; đầy thì bỏ ảnh (vẫn ghi dòng kết quả) và đếm.</summary>
    public int MaxPendingImages { get; set; } = 16;

    /// <summary>
    /// Ghi thêm ảnh KÈM đồ hoạ kết quả (JPEG, <c>..._view.jpg</c>) cạnh mỗi ảnh gốc — QA xem bằng
    /// trình xem ảnh thường mà không cần VisionPro.
    /// </summary>
    public bool SaveAnnotatedImages { get; set; }

    /// <summary>
    /// Mẫu đường dẫn ảnh (tương đối với thư mục ảnh, không phần mở rộng). Chỗ thay: {day} {time}
    /// {code} {serial} {status} {product} {channel}. Phải bắt đầu bằng {day} — bộ dọn đĩa xoá theo
    /// thư mục ngày (Chương 23, mục 23.4.4).
    /// </summary>
    public string ImageNamePattern { get; set; } = "{day}/{code}/{time}_{serial}_{status}";
}

/// <summary>Bộ đếm của bộ ghi — hiện trên màn hình, ghi log khi tắt máy.</summary>
public sealed record RecorderCounters(long Rows, long Images, long DroppedImages, long Failures);

/// <summary>
/// Ghi mọi kết quả ra đĩa (Chương 23, mục 23.4): <c>results\yyyy-MM-dd\&lt;lệnh&gt;.csv</c> (mỗi
/// output một cột, mở được bằng Excel) và ảnh vào <c>images\yyyy-MM-dd\&lt;lệnh&gt;\</c>. Ghi trên
/// MỘT luồng nền: lệnh của máy không bao giờ chờ đĩa. Ảnh chờ ghi có giới hạn — đĩa chậm thì mất
/// ảnh (đếm được), không mất câu trả lời và không ăn hết RAM.
/// </summary>
public sealed class ResultRecorder : IDisposable
{
    private readonly CommandEngine _engine;
    private readonly string _resultsRoot;
    private readonly string _imagesRoot;
    private readonly IImageStore _images;
    private readonly ResultRecorderOptions _options;
    private readonly ILogger<ResultRecorder> _logger;
    private readonly BlockingCollection<CommandResult> _queue = new();
    private readonly Thread _writer;
    private readonly Dictionary<string, int> _okCount = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _openFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _blocked = new(StringComparer.OrdinalIgnoreCase);
    private int _pendingImages;
    private int _stopped;
    private long _rows;
    private long _saved;
    private long _dropped;
    private long _failures;

    public ResultRecorder(CommandEngine engine, string resultsRoot, string imagesRoot,
        IImageStore images, ResultRecorderOptions options, ILogger<ResultRecorder> logger)
    {
        Guard.ThrowIfNull(engine, nameof(engine));
        Guard.ThrowIfNull(resultsRoot, nameof(resultsRoot));
        Guard.ThrowIfNull(imagesRoot, nameof(imagesRoot));
        Guard.ThrowIfNull(images, nameof(images));
        Guard.ThrowIfNull(options, nameof(options));
        Guard.ThrowIfNull(logger, nameof(logger));
        _engine = engine;
        _resultsRoot = resultsRoot;
        _imagesRoot = imagesRoot;
        _images = images;
        _options = options;
        _logger = logger;
        _writer = new Thread(WriteLoop) { IsBackground = true, Name = "ResultRecorder" };
        _writer.Start();
        _engine.Completed += OnCompleted;
    }

    public RecorderCounters Counters => new(Interlocked.Read(ref _rows),
        Interlocked.Read(ref _saved), Interlocked.Read(ref _dropped),
        Interlocked.Read(ref _failures));

    /// <summary>Ngừng nhận, ghi nốt hàng đợi (tối đa <paramref name="wait"/>), trả ảnh còn lại.</summary>
    public void Dispose() => Stop(TimeSpan.FromSeconds(10));

    public void Stop(TimeSpan wait)
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0)
        {
            return;   // bước tắt máy đã dừng; DI giải phóng lần nữa thì bỏ qua
        }
        _engine.Completed -= OnCompleted;
        _queue.CompleteAdding();
        if (!_writer.Join(wait))
        {
            _logger.LogWarning("Result recorder did not finish within {Wait}", wait);
        }
        while (_queue.TryTake(out CommandResult? left))
        {
            left.Frame?.Dispose();
        }
        RecorderCounters c = Counters;
        _logger.LogInformation("Result recorder: {Rows} rows, {Images} images, {Dropped} images " +
                               "dropped, {Failures} failures", c.Rows, c.Images, c.DroppedImages,
            c.Failures);
    }

    private void OnCompleted(object? sender, CommandResultEventArgs e)
    {
        CommandResult r = e.Result;
        DisplayFrame? frame = null;
        if (r.Frame is not null && WantsImage(r))
        {
            if (Interlocked.Increment(ref _pendingImages) <= Math.Max(0, _options.MaxPendingImages))
            {
                frame = r.Frame.Share();   // giữ ảnh TRONG sự kiện (Chương 22)
            }
            else
            {
                Interlocked.Decrement(ref _pendingImages);
                Interlocked.Increment(ref _dropped);
            }
        }
        try
        {
            _queue.Add(r with { Frame = frame });
        }
        catch (InvalidOperationException)
        {
            frame?.Dispose();   // đang tắt máy
        }
    }

    private bool WantsImage(CommandResult r)
    {
        if (r.Status != CommandStatus.Ok)
        {
            return _options.SaveNgImages;
        }
        if (_options.SaveOkImagesEvery <= 0)
        {
            return false;
        }
        lock (_okCount)
        {
            _okCount.TryGetValue(r.Code, out int n);
            _okCount[r.Code] = ++n;
            return n % _options.SaveOkImagesEvery == 0;
        }
    }

    private void WriteLoop()
    {
        foreach (CommandResult r in _queue.GetConsumingEnumerable())
        {
            string image = string.Empty;
            try
            {
                if (r.Frame is not null)
                {
                    image = SaveImage(r);
                }
            }
            catch (Exception ex)
            {
                // Đĩa đầy, mất quyền ghi…: mất ảnh này, không mất dòng kết quả, không dừng bộ ghi.
                Interlocked.Increment(ref _failures);
                _logger.LogError(ex, "Saving image of {Code} {Serial} failed", r.Code, r.Serial);
            }
            finally
            {
                if (r.Frame is not null)
                {
                    r.Frame.Dispose();
                    Interlocked.Decrement(ref _pendingImages);
                }
            }
            try
            {
                AppendRow(r, image);
                Interlocked.Increment(ref _rows);
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref _failures);
                _logger.LogError(ex, "Writing result row of {Code} {Serial} failed", r.Code,
                    r.Serial);
            }
        }
    }

    private string SaveImage(CommandResult r)
    {
        string relative = ImageName(_options.ImageNamePattern, r) + _images.Extension;
        string file = Path.Combine(_imagesRoot, relative);
        _images.Save(r.Frame!.Image, file);
        Interlocked.Increment(ref _saved);
        if (_options.SaveAnnotatedImages && r.Frame.Record is not null)
        {
            string view = Path.Combine(Path.GetDirectoryName(file)!,
                Path.GetFileNameWithoutExtension(file) + "_view.jpg");
            _images.SaveAnnotated(r.Frame.Record, string.Empty, view);
        }
        return relative.Replace('\\', '/');
    }

    /// <summary>Đường dẫn ảnh theo mẫu; mỗi giá trị đã bỏ ký tự cấm trong tên file.</summary>
    internal static string ImageName(string pattern, CommandResult r)
    {
        DateTime t = Time(r);
        string name = pattern
            .Replace("{day}", t.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
            .Replace("{time}", t.ToString("HHmmss_fff", CultureInfo.InvariantCulture))
            .Replace("{code}", Safe(r.Code))
            .Replace("{serial}", Safe(r.Serial.Length == 0 ? "-" : r.Serial))
            .Replace("{status}", r.Status.ToString().ToUpperInvariant())
            .Replace("{product}", Safe(r.ProductId.Length == 0 ? "-" : r.ProductId))
            .Replace("{channel}", Safe(r.Channel.Length == 0 ? "-" : r.Channel));
        return name.Replace('/', Path.DirectorySeparatorChar);
    }

    /// <summary>
    /// Cột <c>error</c> của CSV: khoá lỗi, hoặc với NG thì output nào vượt giới hạn — lý do không còn gửi
    /// máy (DESIGN_LOG [29]) nên file dữ liệu là nơi truy lại vì sao một sản phẩm bị loại.
    /// </summary>
    internal static string Reason(CommandResult r)
    {
        if (r.Error is not null)
        {
            return r.Error.Key;
        }
        if (r.Status != CommandStatus.Ng)
        {
            return string.Empty;
        }
        List<string> over = r.Outputs.Where(o => !o.InLimits).Select(o => o.Name).ToList();
        return over.Count > 0 ? "LIMIT:" + string.Join(";", over) : "REJECT";
    }

    private void AppendRow(CommandResult r, string image)
    {
        DateTime t = Time(r);
        string day = t.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        List<string> header = new() { "time", "serial", "status", "error" };
        header.AddRange(r.Outputs.Select(o => o.Name));
        header.AddRange(new[] { "total_ms", "channel", "product", "version", "image" });
        string headerLine = string.Join(",", header.Select(Csv));

        List<string> row = new()
        {
            t.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture), r.Serial,
            r.Status.ToString().ToUpperInvariant(), Reason(r),
        };
        row.AddRange(r.Outputs.Select(o => o.Text));
        row.AddRange(new[]
        {
            r.Times.TotalMs.ToString(CultureInfo.InvariantCulture), r.Channel, r.ProductId,
            r.ProductVersion, image,
        });
        byte[] line = new UTF8Encoding(false).GetBytes(string.Join(",", row.Select(Csv)) + "\r\n");
        // File đang bị khoá (đo thật: form Lịch sử đọc đúng lúc ghi; QA mở bằng Excel thì Excel
        // khoá cả buổi): thử lại vài lần, vẫn khoá thì bỏ file đó trong phiên này và ghi sang file
        // kế tiếp — không mất dòng nào.
        for (int attempt = 0; ; attempt++)
        {
            string file = FileFor(day, Safe(r.Code), headerLine);
            try
            {
                using FileStream stream = new(file, FileMode.Append, FileAccess.Write,
                    FileShare.ReadWrite);
                stream.Write(line, 0, line.Length);
                return;
            }
            catch (IOException ex) when (attempt < 6)
            {
                if (attempt < 3)
                {
                    Thread.Sleep(50);
                    continue;
                }
                _logger.LogWarning(ex, "{File} is locked, writing to the next file", file);
                _blocked.Add(file);
                _openFiles.Remove(day + "/" + Safe(r.Code) + "/" + headerLine);
            }
        }
    }

    /// <summary>
    /// File CSV của lệnh trong ngày. Lệnh lỗi không có giá trị, và sau một lần áp dụng các output
    /// có thể đổi: tiêu đề khác tiêu đề đang có thì mở file mới (<c>T3_2.csv</c>) — một file CSV
    /// chỉ có MỘT bộ cột, Excel mới đọc đúng.
    /// </summary>
    private string FileFor(string day, string code, string headerLine)
    {
        string key = day + "/" + code + "/" + headerLine;
        if (_openFiles.TryGetValue(key, out string? known))
        {
            return known;
        }
        string folder = Path.Combine(_resultsRoot, day);
        Directory.CreateDirectory(folder);
        for (int n = 1; ; n++)
        {
            string file = Path.Combine(folder, n == 1 ? code + ".csv" : code + "_" + n + ".csv");
            if (_blocked.Contains(file))
            {
                continue;
            }
            if (!File.Exists(file))
            {
                // BOM: Excel nhận ra UTF-8, tên cột tiếng Việt hiện đúng.
                File.WriteAllText(file, headerLine + "\r\n", new UTF8Encoding(true));
                _openFiles[key] = file;
                return file;
            }
            string? first = ResultReader.FirstLine(file);
            if (first == headerLine)
            {
                _openFiles[key] = file;
                return file;
            }
        }
    }

    private static DateTime Time(CommandResult r) =>
        r.ReceivedAt == default ? DateTime.Now : r.ReceivedAt;

    internal static string Csv(string value) =>
        value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0
            ? value
            : "\"" + value.Replace("\"", "\"\"") + "\"";

    private static string Safe(string text) =>
        new(text.Select(c => Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 || c == ' '
            ? '_' : c).ToArray());
}
