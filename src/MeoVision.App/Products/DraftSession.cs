using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using MeoVision.App.Commands;
using MeoVision.App.Hosting;
using MeoVision.Core;
using MeoVision.Core.Commands;
using MeoVision.Core.Devices;
using MeoVision.Core.Localization;
using MeoVision.Core.Products;
using Newtonsoft.Json;

namespace MeoVision.App.Products;

/// <summary>Kết quả mở bản nháp: dùng được, hoặc danh sách lỗi (bản nháp không mở).</summary>
public sealed record DraftOpenResult(DraftSession? Draft, IReadOnlyList<Msg> Problems);

/// <summary>
/// Bản nháp của sản phẩm đang chạy (Chương 22, mục 22.5): một bản chép thư mục sản phẩm, nạp và
/// kiểm bằng chính <see cref="ProductLoader"/>, với các ĐỐI TƯỢNG job riêng. Sửa số, sửa job trong
/// editor, chạy thử — tất cả trên bản nháp; job đang trả lời máy không bao giờ bị đụng tới. Áp dụng
/// và quay lại ở <see cref="ProductStore"/>.
/// </summary>
public sealed class DraftSession : IDisposable
{
    private readonly LoadedProduct _loaded;
    private readonly HashSet<string> _editedJobs = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();
    private readonly ProductConfig _opened;
    private volatile bool _editorOpen;

    internal DraftSession(ProductPaths paths, LoadedProduct loaded, string user)
    {
        Paths = paths;
        _loaded = loaded;
        User = user;
        _opened = Clone(loaded.Config);
    }

    public string ProductId => _loaded.Config.Id;

    /// <summary>Thư mục bản nháp (<c>drafts\&lt;mã&gt;</c>), không phải thư mục sản phẩm.</summary>
    public ProductPaths Paths { get; }

    /// <summary>Người mở bản nháp — ghi vào bản sao lưu khi áp dụng.</summary>
    public string User { get; }

    /// <summary>
    /// Editor của VisionPro đang mở trên một job nháp. Editor sửa ToolBlock không qua khoá của
    /// job, nên trong lúc mở, chạy kèm bỏ lượt thay vì chạy chồng lên chỗ đang sửa.
    /// </summary>
    public bool EditorOpen
    {
        get => _editorOpen;
        set => _editorOpen = value;
    }

    public IReadOnlyCollection<string> EditedJobs
    {
        get { lock (_sync) { return _editedJobs.ToList(); } }
    }

    /// <summary>
    /// Lệnh đã sửa so với lúc mở bản nháp (cấu hình hoặc job)? Lệnh chưa sửa thì chạy kèm chắc chắn
    /// ra giống — không tốn CPU cho nó. Mã không có trong bản nháp: false.
    /// </summary>
    public bool IsChanged(string code)
    {
        lock (_sync)
        {
            CommandConfig? current = Find(code);
            if (current is null)
            {
                return false;
            }
            CommandConfig? opened = _opened.Commands.Find(c => c.Code == current.Code);
            return opened is null || _editedJobs.Contains(current.Job) ||
                   JsonConvert.SerializeObject(opened) != JsonConvert.SerializeObject(current);
        }
    }

    /// <summary>Bản sao cấu hình hiện tại của bản nháp (ghi ra product.json khi áp dụng).</summary>
    public ProductConfig Snapshot()
    {
        lock (_sync)
        {
            return Clone(_loaded.Config);
        }
    }

    /// <summary>Lệnh trong bản nháp (bản sao — sửa qua các hàm Set*); null nếu không có.</summary>
    public CommandConfig? Command(string code)
    {
        lock (_sync)
        {
            CommandConfig? c = Find(code);
            return c is null ? null : Clone(c);
        }
    }

    /// <summary>Job nháp của lệnh — cho editor (<see cref="IVisionJob.EditSubject"/>).</summary>
    public IVisionJob Job(string code)
    {
        lock (_sync)
        {
            return _loaded.Job(Require(code).Job);
        }
    }

    /// <summary>Đánh dấu job đã sửa trong editor: khi áp dụng sẽ ghi file .vpp của nó.</summary>
    public void MarkJobEdited(string code)
    {
        lock (_sync)
        {
            _editedJobs.Add(Require(code).Job);
        }
    }

    /// <summary>Đổi tham số nhanh. Ngoài [min; max] là lỗi người gọi — giao diện đã chặn.</summary>
    public void SetTunable(string code, string terminal, double value)
    {
        lock (_sync)
        {
            TunableConfig tune = Require(code).Tunables.Find(t => t.Terminal == terminal) ??
                                 throw new ArgumentException("Unknown tunable " + terminal);
            if (value < tune.Min || value > tune.Max)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value,
                    $"{terminal} must be in [{tune.Min}; {tune.Max}]");
            }
            tune.Value = value;
        }
    }

    public void SetLimits(string code, string terminal, double? lower, double? upper,
        double offset)
    {
        if (lower is not null && upper is not null && lower > upper)
        {
            throw new ArgumentException($"lower {lower} > upper {upper}");
        }
        lock (_sync)
        {
            OutputConfig output = Require(code).Outputs.Find(o => o.Terminal == terminal) ??
                                  throw new ArgumentException("Unknown output " + terminal);
            output.Lower = lower;
            output.Upper = upper;
            output.Offset = offset;
        }
    }

    /// <summary>
    /// Phơi sáng và đèn: chỉ thử được bằng ảnh chụp mới (OFFLINE) — chạy kèm không đổi được ảnh.
    /// </summary>
    public void SetCapture(string code, double? exposureMs, IReadOnlyList<LightSetting> lights)
    {
        Guard.ThrowIfNull(lights, nameof(lights));
        lock (_sync)
        {
            CommandConfig c = Require(code);
            c.ExposureMs = exposureMs;
            c.Lights = lights.Select(l => new LightSetting
            {
                Role = l.Role, Channel = l.Channel, Intensity = l.Intensity,
            }).ToList();
        }
    }

    /// <summary>
    /// Gắn hiệu chuẩn của lệnh vào ảnh đọc từ file. Ảnh từ lệnh thật đã có hiệu chuẩn rồi.
    /// </summary>
    public void Calibrate(string code, CameraImage image)
    {
        Guard.ThrowIfNull(image, nameof(image));
        string calibration;
        lock (_sync)
        {
            calibration = Require(code).Calibration;
        }
        _loaded.Calibration(calibration)?.Apply(image);
    }

    /// <summary>
    /// Chạy lệnh của bản nháp trên một ảnh có sẵn (ảnh của lệnh thật, vòng ảnh gần nhất, file).
    /// Kết quả mang khung ảnh (bản sao riêng của ảnh nguồn, cùng điểm ảnh) với record của bản nháp —
    /// người nhận phải Dispose khung đó.
    /// Null = bản nháp đã đóng.
    /// </summary>
    public CommandResult? Run(string code, DisplayFrame source, bool withRecord = true)
    {
        Guard.ThrowIfNull(source, nameof(source));
        using IDisposable? lease = _loaded.Acquire();
        if (lease is null)
        {
            return null;
        }
        CommandConfig c;
        lock (_sync)
        {
            // Chụp lại cấu hình: kéo thanh trượt giữa lúc chạy không làm lệch lần chạy này.
            c = Clone(Require(code));
        }
        Stopwatch watch = Stopwatch.StartNew();
        JobRequest request = new(c.Outputs.Select(o => o.Terminal).ToList(),
            c.Tunables.ToDictionary(t => t.Terminal, t => t.Value, StringComparer.Ordinal),
            withRecord);
        // Ảnh của lệnh thật / vòng ảnh gần nhất đã có hệ toạ độ do job ĐANG CHẠY gắn vào (Fixture);
        // job nháp là đối tượng khác, chạy thẳng trên ảnh đó thì Fixture của nó báo lỗi → chạy trên
        // bản sao có cây hệ toạ độ riêng (DESIGN_LOG [31]).
        CameraImage? own = source.Image.CopyForAnotherJob();
        JobRun run;
        try
        {
            run = _loaded.Job(c.Job).Run(own ?? source.Image, request);
        }
        catch
        {
            own?.Dispose();
            throw;
        }
        Judgement judgement = ResultJudge.Judge(c, run);
        long ms = watch.ElapsedMilliseconds;
        DisplayFrame frame = own is null ? source.ShareWith(run.Record)
            : new DisplayFrame(own, run.Record);
        return new CommandResult(c.Code, judgement.Status, judgement.Error, judgement.Outputs,
            new StepTimes(0, 0, 0, 0, ms, ms, 0)) { Frame = frame };
    }

    /// <summary>
    /// Những gì bản nháp khác sản phẩm đang chạy — hiện trước khi áp dụng và ghi vào log.
    /// </summary>
    public IReadOnlyList<Msg> Changes(ProductConfig running)
    {
        Guard.ThrowIfNull(running, nameof(running));
        List<Msg> changes = new();
        lock (_sync)
        {
            foreach (CommandConfig d in _loaded.Config.Commands)
            {
                CommandConfig? r = running.Commands.Find(c => c.Code == d.Code);
                if (r is null)
                {
                    changes.Add(Msg.Of("draft.change.added", d.Code));
                    continue;
                }
                CompareCommand(r, d, changes);
                if (_editedJobs.Contains(d.Job))
                {
                    changes.Add(Msg.Of("draft.change.job", d.Code, d.Job));
                }
            }
        }
        return changes;
    }

    /// <summary>Bản nháp có đổi phơi sáng/đèn không — thứ chạy kèm không thử được.</summary>
    public bool CaptureChanged(string code, ProductConfig running)
    {
        Guard.ThrowIfNull(running, nameof(running));
        CommandConfig? r = running.Commands.Find(c => c.Code == code);
        lock (_sync)
        {
            CommandConfig d = Require(code);
            return r is null || r.ExposureMs != d.ExposureMs ||
                   JsonConvert.SerializeObject(r.Lights) != JsonConvert.SerializeObject(d.Lights);
        }
    }

    /// <summary>Ghi job đã sửa và product.json vào thư mục NHÁP — bước đầu của áp dụng.</summary>
    internal ProductConfig WriteFiles()
    {
        lock (_sync)
        {
            foreach (string job in _editedJobs)
            {
                _loaded.Job(job).Save(Paths.Job(job));
            }
            return Clone(_loaded.Config);
        }
    }

    /// <summary>Đóng bản nháp: job nháp đóng khi lần chạy cuối (nếu đang chạy) trả lại.</summary>
    public void Dispose() => _loaded.Retire();

    private static void CompareCommand(CommandConfig r, CommandConfig d, List<Msg> changes)
    {
        if (r.ExposureMs != d.ExposureMs)
        {
            changes.Add(Msg.Of("draft.change.exposure", d.Code, Text(r.ExposureMs),
                Text(d.ExposureMs)));
        }
        foreach (LightSetting light in d.Lights)
        {
            LightSetting? old = r.Lights.Find(l => l.Role == light.Role &&
                                                   l.Channel == light.Channel);
            if (old?.Intensity != light.Intensity)
            {
                changes.Add(Msg.Of("draft.change.light", d.Code, light.Role, light.Channel,
                    old is null ? "-" : Text(old.Intensity), Text(light.Intensity)));
            }
        }
        foreach (TunableConfig tune in d.Tunables)
        {
            TunableConfig? old = r.Tunables.Find(t => t.Terminal == tune.Terminal);
            if (old is null || old.Value != tune.Value)
            {
                changes.Add(Msg.Of("draft.change.tunable", d.Code, tune.Terminal,
                    old is null ? "-" : Text(old.Value), Text(tune.Value)));
            }
        }
        foreach (OutputConfig output in d.Outputs)
        {
            OutputConfig? old = r.Outputs.Find(o => o.Terminal == output.Terminal);
            if (old is null || old.Lower != output.Lower || old.Upper != output.Upper ||
                old.Offset != output.Offset)
            {
                changes.Add(Msg.Of("draft.change.limits", d.Code, output.Terminal,
                    old is null ? "-" : Range(old), Range(output)));
            }
        }
    }

    private static string Range(OutputConfig o) =>
        $"[{Text(o.Lower)}; {Text(o.Upper)}] {(o.Offset >= 0 ? "+" : "")}{Text(o.Offset)}";

    private static string Text(double? value) =>
        value?.ToString("0.###", CultureInfo.InvariantCulture) ?? "-";

    private CommandConfig? Find(string code) =>
        _loaded.Config.Commands.Find(c => string.Equals(c.Code, code,
            StringComparison.OrdinalIgnoreCase));

    private CommandConfig Require(string code) =>
        Find(code) ?? throw new ArgumentException("Unknown command " + code, nameof(code));

    private static T Clone<T>(T value) =>
        JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(value))!;
}
