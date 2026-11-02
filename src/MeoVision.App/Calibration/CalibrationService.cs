using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MeoVision.App.Comm;
using MeoVision.App.Devices;
using MeoVision.App.Hosting;
using MeoVision.App.Products;
using MeoVision.Core;
using MeoVision.Core.Calibration;
using MeoVision.Core.Commands;
using MeoVision.Core.Devices;
using MeoVision.Core.Localization;
using MeoVision.Core.Products;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace MeoVision.App.Calibration;

/// <summary>Một hiệu chuẩn của máy, cho form Hiệu chuẩn (DESIGN_LOG [28] D5).</summary>
/// <param name="SavedAt">Lúc ghi <c>calibration.vpp</c> đang dùng; null = chưa có.</param>
/// <param name="LastAttemptAt">Lúc ghi <c>points.csv</c> — lần tính gần nhất, có thể đã bị từ chối vì RMS.</param>
/// <param name="LastRms">RMS (pixel) của lần tính gần nhất, từ sai số từng điểm.</param>
/// <param name="PointsInSession">Số điểm đã nhận của phiên máy đang dẫn dở; null = không có phiên.</param>
public sealed record CalibrationInfo(string Id, string Camera, double MaxRms, DateTime? SavedAt,
    DateTime? LastAttemptAt, int LastPoints, double? LastRms, int HistoryCount, int? PointsInSession);

/// <summary>Một dòng của <c>points.csv</c>: điểm và sai số của nó sau khi tính.</summary>
public sealed record CalibrationPointRow(int Index, double PixelX, double PixelY, double MachineX,
    double MachineY, double Residual);

/// <summary>Trả lời một lệnh hiệu chuẩn: được/không, lý do ngắn, các giá trị gửi lại máy.</summary>
public sealed record CalibrationReply(bool Ok, string Reason, IReadOnlyList<string> Values)
{
    public static CalibrationReply Fail(string reason, params string[] values) =>
        new(false, reason, values);
}

/// <summary>
/// Hiệu chuẩn do máy dẫn (Chương 7, mục 7.7.2; Chương 21, mục 21.5). Ba lệnh:
/// <c>CS,&lt;mã&gt;</c> bắt đầu ·
/// <c>CP,&lt;mã&gt;,&lt;i&gt;,&lt;X&gt;,&lt;Y&gt;</c> máy đã tới điểm i, vision chụp và tìm dấu ·
/// <c>CE,&lt;mã&gt;</c> tính, kiểm RMS, ghi <c>calibration.vpp</c> (bản cũ vào <c>history</c>)
/// rồi nạp lại sản phẩm để lệnh kế tiếp dùng hiệu chuẩn mới.
/// </summary>
public sealed class CalibrationService : IDisposable
{
    public const string ConfigFile = "calibration.json";
    public const string ResultFile = "calibration.vpp";

    private static readonly Regex IdPattern = new("^[A-Za-z0-9_-]{1,32}$", RegexOptions.Compiled);
    private static readonly JsonSerializerSettings JsonSettings = new()
    {
        MissingMemberHandling = MissingMemberHandling.Error,
    };

    private readonly AppPaths _paths;
    private readonly DeviceManager _devices;
    private readonly IJobInspector _inspector;
    private readonly ICalibrationBuilder _builder;
    private readonly ProductStore _store;
    private readonly Func<MachineView> _machine;
    private readonly ILogger<CalibrationService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, Session> _sessions = new(StringComparer.OrdinalIgnoreCase);

    public CalibrationService(AppPaths paths, DeviceManager devices, IJobInspector inspector,
        ICalibrationBuilder builder, ProductStore store, Func<MachineView> machine,
        ILogger<CalibrationService> logger)
    {
        Guard.ThrowIfNull(paths, nameof(paths));
        Guard.ThrowIfNull(devices, nameof(devices));
        Guard.ThrowIfNull(inspector, nameof(inspector));
        Guard.ThrowIfNull(builder, nameof(builder));
        Guard.ThrowIfNull(store, nameof(store));
        Guard.ThrowIfNull(machine, nameof(machine));
        Guard.ThrowIfNull(logger, nameof(logger));
        _paths = paths;
        _devices = devices;
        _inspector = inspector;
        _builder = builder;
        _store = store;
        _machine = machine;
        _logger = logger;
    }

    public async Task<CalibrationReply> HandleAsync(Request request, CancellationToken ct = default)
    {
        Guard.ThrowIfNull(request, nameof(request));
        string id = request.Field(0);
        if (!IdPattern.IsMatch(id))
        {
            return CalibrationReply.Fail("BAD_FORMAT");
        }
        // Một lệnh hiệu chuẩn một lúc: máy dẫn tuần tự, và lệnh lạc thứ tự không được chen nhau.
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return request.Code switch
            {
                CommandService.CalibrationStart => Start(id),
                CommandService.CalibrationPointCode => await AddPointAsync(id, request, ct)
                    .ConfigureAwait(false),
                _ => Finish(id),
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    // ---------------- form Hiệu chuẩn (DESIGN_LOG [28] D5) ----------------

    /// <summary>Mọi hiệu chuẩn của máy (mỗi thư mục con của <c>machine\calibration</c>).</summary>
    public IReadOnlyList<CalibrationInfo> List()
    {
        List<CalibrationInfo> list = new();
        if (!Directory.Exists(_paths.MachineCalibration))
        {
            return list;
        }
        foreach (string folder in Directory.GetDirectories(_paths.MachineCalibration)
                     .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            string id = Path.GetFileName(folder);
            CalibrationConfig? config = null;
            try
            {
                string file = Path.Combine(folder, ConfigFile);
                config = File.Exists(file)
                    ? JsonConvert.DeserializeObject<CalibrationConfig>(
                        File.ReadAllText(file, Encoding.UTF8), JsonSettings)
                    : null;
            }
            catch (JsonException)
            {
                // Cấu hình hỏng vẫn liệt kê — form cho thấy nó có ở đó.
            }
            string vpp = Path.Combine(folder, ResultFile);
            string csv = Path.Combine(folder, "points.csv");
            IReadOnlyList<CalibrationPointRow> points = Points(id);
            string history = Path.Combine(folder, "history");
            int? inSession;
            lock (_sessions)
            {
                inSession = _sessions.TryGetValue(id, out Session? s) ? s.Points.Count : null;
            }
            list.Add(new CalibrationInfo(id, config?.Camera ?? "?", config?.MaxRmsPixels ?? double.NaN,
                File.Exists(vpp) ? File.GetLastWriteTime(vpp) : null,
                File.Exists(csv) ? File.GetLastWriteTime(csv) : null, points.Count,
                points.Count == 0 || points.Any(p => double.IsNaN(p.Residual))
                    ? null
                    : Math.Sqrt(points.Average(p => p.Residual * p.Residual)),
                Directory.Exists(history) ? Directory.GetFiles(history, "*.vpp").Length : 0,
                inSession));
        }
        return list;
    }

    /// <summary>Các điểm của lần tính gần nhất (<c>points.csv</c>).</summary>
    public IReadOnlyList<CalibrationPointRow> Points(string id)
    {
        Guard.ThrowIfNull(id, nameof(id));
        string file = Path.Combine(_paths.MachineCalibration, id, "points.csv");
        List<CalibrationPointRow> rows = new();
        if (!IdPattern.IsMatch(id) || !File.Exists(file))
        {
            return rows;
        }
        foreach (string line in File.ReadAllLines(file).Skip(1))
        {
            string[] f = line.Split(',');
            if (f.Length >= 6 && int.TryParse(f[0], NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out int index))
            {
                rows.Add(new CalibrationPointRow(index, Number(f[1]), Number(f[2]), Number(f[3]),
                    Number(f[4]), Number(f[5])));
            }
        }
        return rows;
    }

    /// <summary>Các bản cũ (mới nhất trước), tên file trong <c>history</c>.</summary>
    public IReadOnlyList<string> History(string id)
    {
        Guard.ThrowIfNull(id, nameof(id));
        string folder = Path.Combine(_paths.MachineCalibration, id, "history");
        if (!IdPattern.IsMatch(id) || !Directory.Exists(folder))
        {
            return Array.Empty<string>();
        }
        return Directory.GetFiles(folder, "*.vpp").Select(f => Path.GetFileName(f))
            .OrderByDescending(n => n, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Dùng lại một bản cũ: bản đang dùng vào <c>history</c> trước (quay lại được lần nữa), rồi nạp lại
    /// sản phẩm. Sản phẩm không nạp được với bản cũ thì trả lại bản vừa thay. Null = thành công.
    /// </summary>
    public Msg? RestoreFromHistory(string id, string historyFile, string user)
    {
        Guard.ThrowIfNull(id, nameof(id));
        Guard.ThrowIfNull(historyFile, nameof(historyFile));
        Guard.ThrowIfNull(user, nameof(user));
        string folder = Path.Combine(_paths.MachineCalibration, id);
        string source = Path.Combine(folder, "history", Path.GetFileName(historyFile));
        string target = Path.Combine(folder, ResultFile);
        if (!IdPattern.IsMatch(id) || !File.Exists(source))
        {
            return Msg.Of("calibration.noHistory", id, historyFile);
        }
        _gate.Wait();   // không chen giữa một lệnh hiệu chuẩn máy đang gửi
        try
        {
            string? previous = null;
            if (File.Exists(target))
            {
                previous = Path.Combine(folder, "history", "calibration_" +
                    DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) +
                    "_before-restore.vpp");
                File.Copy(target, previous, overwrite: true);
            }
            File.Copy(source, target, overwrite: true);
            string? active = _store.ActiveId;
            if (active is not null && !_store.Switch(active, _machine(), user).IsValid)
            {
                if (previous is not null)
                {
                    File.Copy(previous, target, overwrite: true);
                    _store.Switch(active, _machine(), user);
                }
                _logger.LogWarning("Calibration {Id}: product {Product} rejected {File}; kept the current one",
                    id, active, historyFile);
                return Msg.Of("calibration.restoreRejected", id, historyFile, active);
            }
            _logger.LogInformation("Calibration {Id} restored from {File} by {User}", id, historyFile,
                user);
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Bỏ phiên máy đang dẫn dở (máy dừng giữa chừng); hiệu chuẩn đang dùng giữ nguyên.</summary>
    public bool CancelSession(string id)
    {
        Guard.ThrowIfNull(id, nameof(id));
        _gate.Wait();
        try
        {
            if (!_sessions.TryGetValue(id, out Session? s))
            {
                return false;
            }
            s.Job.Dispose();
            lock (_sessions)
            {
                _sessions.Remove(id);
            }
            _logger.LogInformation("Calibration {Id}: session cancelled ({Count} points)", id,
                s.Points.Count);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static double Number(string text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)
            ? v
            : double.NaN;

    public void Dispose()
    {
        foreach (Session session in _sessions.Values)
        {
            session.Job.Dispose();
        }
        _sessions.Clear();
        _gate.Dispose();
    }

    private CalibrationReply Start(string id)
    {
        string folder = Path.Combine(_paths.MachineCalibration, id);
        string file = Path.Combine(folder, ConfigFile);
        CalibrationConfig? config;
        try
        {
            config = File.Exists(file)
                ? JsonConvert.DeserializeObject<CalibrationConfig>(
                    File.ReadAllText(file, Encoding.UTF8), JsonSettings)
                : null;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Calibration {Id}: {File} is invalid", id, file);
            return CalibrationReply.Fail("CONFIG");
        }
        if (config is null || config.SchemaVersion != CalibrationConfig.CurrentSchemaVersion ||
            !_devices.CameraRoles.Contains(config.Camera, StringComparer.OrdinalIgnoreCase) ||
            config.MinPoints < 3 || config.MaxRmsPixels <= 0)
        {
            _logger.LogError("Calibration {Id}: {File} missing or not usable on this machine", id,
                file);
            return CalibrationReply.Fail("CONFIG");
        }

        // Job tìm dấu nạp qua cùng bộ kiểm job của sản phẩm (Chương 19): hộp thoại script được
        // canh, terminal được kiểm — ở đây cần InputImage và hai output X, Y.
        JobInspection mark = _inspector.Inspect(Path.Combine(folder, config.MarkJob));
        bool usable = mark.LoadError is null && mark.ScriptError is null && mark.Job is not null &&
                      mark.Inputs.Any(t => t.Name == JobContract.InputImage) &&
                      mark.Outputs.Any(t => t.Name == "X") && mark.Outputs.Any(t => t.Name == "Y");
        if (!usable)
        {
            mark.Job?.Dispose();
            _logger.LogError("Calibration {Id}: mark job {Job} not usable ({Error})", id,
                config.MarkJob, mark.LoadError ?? mark.ScriptError);
            return CalibrationReply.Fail("MARK_JOB");
        }

        if (_sessions.TryGetValue(id, out Session? old))
        {
            old.Job.Dispose();   // bắt đầu lại giữa chừng: bỏ hết các điểm cũ
        }
        lock (_sessions)   // form Hiệu chuẩn đọc danh sách phiên từ luồng giao diện
        {
            _sessions[id] = new Session(config, mark.Job!, folder);
        }
        _logger.LogInformation("Calibration {Id} started: camera {Camera}, at least {Min} points",
            id, config.Camera, config.MinPoints);
        return new CalibrationReply(true, string.Empty, Array.Empty<string>());
    }

    private async Task<CalibrationReply> AddPointAsync(string id, Request r, CancellationToken ct)
    {
        if (!_sessions.TryGetValue(id, out Session? s))
        {
            return CalibrationReply.Fail("NO_SESSION");
        }
        // Toạ độ máy luôn viết kiểu bất biến (dấu chấm) — máy không gửi dấu phẩy thập phân.
        if (!int.TryParse(r.Field(1), NumberStyles.Integer, CultureInfo.InvariantCulture,
                out int index) || index < 1 ||
            !double.TryParse(r.Field(2), NumberStyles.Float, CultureInfo.InvariantCulture,
                out double machineX) ||
            !double.TryParse(r.Field(3), NumberStyles.Float, CultureInfo.InvariantCulture,
                out double machineY))
        {
            return CalibrationReply.Fail("BAD_FORMAT");
        }

        CameraImage image;
        try
        {
            foreach (IGrouping<string, LightSetting> group in s.Config.Lights.GroupBy(l => l.Role,
                         StringComparer.OrdinalIgnoreCase))
            {
                await _devices.SetLightsAsync(group.Key, group
                    .Select(l => new LightChannelSetting(l.Channel, l.Intensity)).ToList(), ct)
                    .ConfigureAwait(false);
            }
            image = await _devices.GrabAsync(s.Config.Camera,
                new CaptureSettings(s.Config.ExposureMs), ct).ConfigureAwait(false);
        }
        catch (DeviceException ex)
        {
            _logger.LogWarning(ex, "Calibration {Id} point {Index}: no image", id, index);
            return CalibrationReply.Fail("CAMERA", Text(index));
        }

        using (image)
        {
            JobRun run = s.Job.Run(image, new[] { "X", "Y" });
            if (run.Status is JobRunStatus.Error or JobRunStatus.Reject ||
                run.Outputs["X"] is not IConvertible xv || run.Outputs["Y"] is not IConvertible yv)
            {
                _logger.LogWarning("Calibration {Id} point {Index}: mark not found ({Message})",
                    id, index, run.Message);
                return CalibrationReply.Fail("MARK_NOT_FOUND", Text(index));
            }
            double px = xv.ToDouble(CultureInfo.InvariantCulture);
            double py = yv.ToDouble(CultureInfo.InvariantCulture);
            s.Points[index] = new CalibrationPoint(index, px, py, machineX, machineY);
            return new CalibrationReply(true, string.Empty,
                new[] { Text(index), Text(px), Text(py) });
        }
    }

    private CalibrationReply Finish(string id)
    {
        if (!_sessions.TryGetValue(id, out Session? s))
        {
            return CalibrationReply.Fail("NO_SESSION");
        }
        List<CalibrationPoint> points = s.Points.Values.OrderBy(p => p.Index).ToList();
        if (points.Count < s.Config.MinPoints)
        {
            return CalibrationReply.Fail("TOO_FEW_POINTS", Text(points.Count));
        }

        string target = Path.Combine(s.Folder, ResultFile);
        string candidate = target + ".new";
        CalibrationFit fit = _builder.Fit(points, candidate);
        if (!fit.Ok)
        {
            _logger.LogError("Calibration {Id}: fit failed ({Error})", id, fit.Error);
            return CalibrationReply.Fail("FIT");
        }
        WritePoints(Path.Combine(s.Folder, "points.csv"), points, fit);
        if (fit.Rms > s.Config.MaxRmsPixels)
        {
            // Không ghi đè hiệu chuẩn đang dùng bằng một kết quả kém; điểm đã lưu để xem điểm nào
            // sai.
            File.Delete(candidate);
            _logger.LogWarning("Calibration {Id}: RMS {Rms:0.000} px > {Max} px — kept the old one",
                id, fit.Rms, s.Config.MaxRmsPixels);
            return CalibrationReply.Fail("RMS", Text(fit.Rms), Text(points.Count));
        }

        if (File.Exists(target))
        {
            string history = Path.Combine(s.Folder, "history");
            Directory.CreateDirectory(history);
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            File.Copy(target, Path.Combine(history, "calibration_" + stamp + ".vpp"),
                overwrite: true);
            File.Replace(candidate, target, null);
        }
        else
        {
            File.Move(candidate, target);
        }
        s.Job.Dispose();
        lock (_sessions)
        {
            _sessions.Remove(id);
        }
        _logger.LogInformation("Calibration {Id} saved: {Count} points, RMS {Rms:0.000} px", id,
            points.Count, fit.Rms);

        // Bộ máy chạy lệnh giữ hiệu chuẩn đã nạp (Chương 20); nạp lại sản phẩm đang chạy để lệnh
        // kế tiếp dùng bản mới — đổi giữa hai lệnh, như chuyển sản phẩm.
        string? active = _store.ActiveId;
        if (active is not null && !_store.Switch(active, _machine(), "calibration " + id).IsValid)
        {
            _logger.LogError("Calibration {Id}: product {Product} did not reload", id, active);
        }
        return new CalibrationReply(true, string.Empty,
            new[] { Text(fit.Rms), Text(points.Count) });
    }

    private static void WritePoints(string file, List<CalibrationPoint> points, CalibrationFit fit)
    {
        StringBuilder csv = new("index,pixel_x,pixel_y,machine_x,machine_y,residual_px\r\n");
        for (int i = 0; i < points.Count; i++)
        {
            CalibrationPoint p = points[i];
            double residual = i < fit.Residuals.Count ? fit.Residuals[i] : double.NaN;
            csv.Append(string.Join(",", Text(p.Index), Text(p.PixelX), Text(p.PixelY),
                Text(p.MachineX), Text(p.MachineY), Text(residual))).Append("\r\n");
        }
        File.WriteAllText(file, csv.ToString(), Encoding.ASCII);
    }

    private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Text(double value) =>
        value.ToString("0.000", CultureInfo.InvariantCulture);

    private sealed class Session
    {
        public Session(CalibrationConfig config, IVisionJob job, string folder)
        {
            Config = config;
            Job = job;
            Folder = folder;
        }

        public CalibrationConfig Config { get; }

        public IVisionJob Job { get; }

        public string Folder { get; }

        /// <summary>
        /// Theo số thứ tự điểm: máy gửi lại điểm i (chụp lại) thì thay, không cộng thêm.
        /// </summary>
        public SortedDictionary<int, CalibrationPoint> Points { get; } = new();
    }
}
