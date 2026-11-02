using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MeoVision.App.Devices;
using MeoVision.App.Products;
using MeoVision.Core;
using MeoVision.Core.Commands;
using MeoVision.Core.Devices;
using MeoVision.Core.Localization;
using MeoVision.Core.Products;
using Microsoft.Extensions.Logging;

namespace MeoVision.App.Commands;

/// <summary>
/// Bộ máy chạy lệnh (Chương 20): nhận một mã lệnh, chạy đúng dòng của bảng lệnh — bật đèn → chụp
/// (chụp lại khi lỗi lẻ) → gắn hiệu chuẩn → chạy job → kết luận — trong thời hạn của lệnh, và trả
/// về một <see cref="CommandResult"/> có kiểu, kể cả khi lỗi. Mỗi camera một "làn": lệnh của cùng
/// camera xếp hàng, lệnh của hai camera chạy song song.
/// </summary>
public sealed class CommandEngine : ICommandRunner, IDisposable
{
    private readonly ProductStore _store;
    private readonly DeviceManager _devices;
    private readonly ILogger<CommandEngine> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _lanes =
        new(StringComparer.OrdinalIgnoreCase);

    public CommandEngine(ProductStore store, DeviceManager devices, ILogger<CommandEngine> logger)
    {
        Guard.ThrowIfNull(store, nameof(store));
        Guard.ThrowIfNull(devices, nameof(devices));
        Guard.ThrowIfNull(logger, nameof(logger));
        _store = store;
        _devices = devices;
        _logger = logger;
    }

    /// <summary>
    /// Tạo record hiển thị cho mỗi lệnh (Chương 22). Đo thật: 0,6–3 ms một lần tuỳ job, và job
    /// Blob giữ thêm ~220 MB RAM tới khi bị đóng. Tắt khi không ai nhìn màn hình.
    /// </summary>
    public bool CreateDisplayRecords { get; set; } = true;

    /// <summary>
    /// Lệnh đang bỏ qua (DESIGN_LOG [28] D1) trả <see cref="CommandStatus.Bypassed"/> ngay, không đụng
    /// đèn/camera/job — camera hỏng thì chụp cũng chỉ thêm chờ hết hạn. Null = không dùng.
    /// </summary>
    public BypassService? Bypass { get; set; }

    /// <summary>Dịch thông điệp cho log (ngôn ngữ cố định của log, Chương 17).</summary>
    public Func<Msg, string> FormatForLog { get; set; } = m => m.ToString();

    /// <summary>Mỗi lệnh xong (kể cả lỗi) — giao diện, lưu kết quả (Chương 22–23) nghe.</summary>
    public event EventHandler<CommandResultEventArgs>? Completed;

    /// <summary>
    /// Chạy lệnh <paramref name="code"/> nhận từ <paramref name="channel"/>. KHÔNG ném lỗi vì
    /// thiết bị hay job: mọi thất bại thành <see cref="CommandStatus.Error"/> kèm lý do, để lớp
    /// giao tiếp luôn có thứ trả lời máy. Chỉ ném khi <paramref name="ct"/> bị huỷ (tắt máy).
    /// </summary>
    public async Task<CommandResult> ExecuteAsync(string channel, string code, string serial = "",
        CancellationToken ct = default)
    {
        Guard.ThrowIfNull(channel, nameof(channel));
        Guard.ThrowIfNull(code, nameof(code));
        Guard.ThrowIfNull(serial, nameof(serial));
        Stopwatch total = Stopwatch.StartNew();
        Trace trace = new(channel, serial, DateTime.Now, string.Empty, string.Empty);

        (LoadedProduct Product, IDisposable Lease)? borrowed = _store.Acquire();
        if (borrowed is not { } active)
        {
            return Finish(trace, CommandResult.Failed(code, Msg.Of("cmd.noProduct"), Times(total)));
        }
        trace = trace with
        {
            ProductId = active.Product.Config.Id, ProductVersion = active.Product.Version,
        };
        CommandConfig? command = active.Product.Config.Commands.Find(c =>
            string.Equals(c.Code, code, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(c.Channel, channel, StringComparison.OrdinalIgnoreCase));
        if (command is null)
        {
            active.Lease.Dispose();
            return Finish(trace, CommandResult.Failed(code, Msg.Of("cmd.unknown", code, channel,
                active.Product.Config.Id), Times(total)));
        }
        // Lệnh dẫn hướng không bao giờ bỏ qua — máy cần toạ độ thật (DESIGN_LOG [29]); chặn ở đây cả khi
        // bỏ qua được bật trước khi đổi sang sản phẩm có cùng mã mà là lệnh dẫn hướng.
        if (Bypass is not null && !CommandKind.IsGuide(command.Kind) &&
            Bypass.TryUse(command.Code, out BypassEntry? bypass))
        {
            active.Lease.Dispose();
            return Finish(trace, CommandResult.Bypassed(code, Msg.Of("cmd.bypassed", bypass!.User,
                bypass.Reason), Times(total)));
        }

        // Thời hạn tính từ lúc NHẬN lệnh, gồm cả lúc xếp hàng — máy chỉ biết nó đã chờ bao lâu.
        TimeSpan budget = TimeSpan.FromMilliseconds(command.TimeoutMs);
        // Lệnh nhiều camera giữ làn của MỌI camera nó dùng. Lấy theo thứ tự tên cố định: hai lệnh
        // cùng cần TopCam + BottomCam không bao giờ mỗi lệnh giữ một làn rồi chờ nhau.
        List<SemaphoreSlim> lanes = new();
        string? busy = null;
        try
        {
            foreach (string camera in new[] { command.Camera }
                         .Concat(command.ExtraCameras.Select(e => e.Camera))
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                SemaphoreSlim lane = _lanes.GetOrAdd(camera, _ => new SemaphoreSlim(1, 1));
                TimeSpan left = budget - total.Elapsed;
                if (!await lane.WaitAsync(left > TimeSpan.Zero ? left : TimeSpan.Zero, ct)
                        .ConfigureAwait(false))
                {
                    busy = camera;
                    break;
                }
                lanes.Add(lane);
            }
        }
        catch (OperationCanceledException)
        {
            ReleaseLanes(lanes);
            active.Lease.Dispose();
            throw;
        }
        if (busy is not null)
        {
            ReleaseLanes(lanes);
            active.Lease.Dispose();
            return Finish(trace, CommandResult.Failed(code, Msg.Of("cmd.laneBusy", busy,
                command.TimeoutMs), Times(total)));
        }

        long queueMs = total.ElapsedMilliseconds;
        TimeSpan remaining = budget - total.Elapsed;
        if (remaining < TimeSpan.Zero)
        {
            remaining = TimeSpan.Zero;
        }
        CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(remaining);
        Task<CommandResult> work = Task.Run(() =>
            RunAsync(command, active.Product, total, queueMs, deadline.Token));
        // Làn, lease và bộ hẹn giờ chỉ được trả khi công việc THẬT SỰ xong: job VisionPro không
        // dừng giữa chừng được, lệnh sau của camera này không được chạy chồng lên nó.
        _ = work.ContinueWith(t =>
        {
            if (t.IsFaulted)
            {
                _logger.LogError(t.Exception, "Command {Code} failed unexpectedly", code);
            }
            deadline.Dispose();
            active.Lease.Dispose();
            ReleaseLanes(lanes);
        }, TaskScheduler.Default);

        Task finished = await Task.WhenAny(work, Task.Delay(remaining, ct)).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        if (finished == work && work.Status == TaskStatus.RanToCompletion)
        {
            return Finish(trace, work.Result);
        }
        if (finished == work && work.IsFaulted)
        {
            Exception error = work.Exception!.GetBaseException();
            return Finish(trace, CommandResult.Failed(code, Msg.Of("cmd.unexpected",
                error.GetType().Name, error.Message), Times(total)));
        }

        // Quá hạn: trả lời máy NGAY; phần đang chạy (thường là job) xong muộn thì chỉ ghi log.
        _ = work.ContinueWith(t =>
        {
            _logger.LogWarning("Command {Code} finished {Late} ms after its {Timeout} ms deadline",
                code, total.ElapsedMilliseconds - command.TimeoutMs, command.TimeoutMs);
            if (t.Status == TaskStatus.RanToCompletion)
            {
                t.Result.Frame?.Dispose();   // kết quả muộn không ai nhận — trả ảnh
            }
        }, TaskScheduler.Default);
        return Finish(trace, CommandResult.Failed(code, Msg.Of("cmd.timeout", code, command.TimeoutMs),
            Times(total, queueMs)));
    }

    private static void ReleaseLanes(List<SemaphoreSlim> lanes)
    {
        foreach (SemaphoreSlim lane in lanes)
        {
            lane.Release();
        }
    }

    public void Dispose()
    {
        foreach (SemaphoreSlim lane in _lanes.Values)
        {
            lane.Dispose();
        }
    }

    private async Task<CommandResult> RunAsync(CommandConfig c, LoadedProduct product,
        Stopwatch total, long queueMs, CancellationToken ct)
    {
        long mark = total.ElapsedMilliseconds;
        long Lap()
        {
            long now = total.ElapsedMilliseconds;
            long lap = now - mark;
            mark = now;
            return lap;
        }

        // 1. Đèn — mỗi bộ điều khiển một lời gọi. Không bật được đèn thì KHÔNG chụp: ảnh tối
        //    vẫn chạy job được và ra một kết luận sai mà không ai biết.
        try
        {
            foreach (IGrouping<string, LightSetting> group in c.Lights.GroupBy(l => l.Role,
                         StringComparer.OrdinalIgnoreCase))
            {
                await _devices.SetLightsAsync(group.Key, group
                    .Select(l => new LightChannelSetting(l.Channel, l.Intensity))
                    .ToList(), ct).ConfigureAwait(false);
            }
        }
        catch (DeviceException ex)
        {
            return CommandResult.Failed(c.Code, Msg.Of("cmd.lightFailed", ex.Info),
                Times(total, queueMs, Lap()));
        }
        // Đèn công suất lớn cần vài chục ms mới sáng ổn định: chụp ngay là ảnh tối/nhấp nháy.
        int settle = c.Lights.Count == 0 ? 0 : c.Lights.Max(l => l.SettleMs);
        if (settle > 0)
        {
            await Task.Delay(settle, ct).ConfigureAwait(false);
        }
        long lightMs = Lap();

        // 2. Chụp — camera chính rồi các camera thêm. Chụp lại chỉ khi lỗi LẺ (khung hụt); camera
        //    hỏng thì chụp lại vô ích.
        CameraImage? image = null;
        List<CameraImage> extras = new();
        int attempts = 0;
        try
        {
            image = await GrabWithRetryAsync(c, c.Camera,
                new CaptureSettings(c.ExposureMs, c.Brightness, c.Contrast), n => attempts += n,
                ct).ConfigureAwait(false);
            foreach (CameraInput extra in c.ExtraCameras)
            {
                extras.Add(await GrabWithRetryAsync(c, extra.Camera,
                    new CaptureSettings(extra.ExposureMs, extra.Brightness, extra.Contrast),
                    n => attempts += n, ct).ConfigureAwait(false));
            }
        }
        catch (GrabFailedException failed)
        {
            image?.Dispose();
            foreach (CameraImage e in extras)
            {
                e.Dispose();
            }
            return CommandResult.Failed(c.Code, Msg.Of("cmd.grabFailed", failed.Camera,
                    failed.Info),
                new StepTimes(queueMs, lightMs, Lap(), 0, 0, total.ElapsedMilliseconds, attempts));
        }
        long grabMs = Lap();

        // Từ đây ảnh thuộc về khung hiển thị (Chương 22): khung sống tới khi người giữ cuối trả.
        DisplayFrame? frame = null;
        try
        {
            // 3. Hiệu chuẩn — gắn hệ toạ độ mm vào chính ảnh này (mỗi ảnh hiệu chuẩn của nó).
            product.Calibration(c.Calibration)?.Apply(image);
            Dictionary<string, CameraImage> inputs = new(StringComparer.Ordinal);
            for (int i = 0; i < extras.Count; i++)
            {
                product.Calibration(c.ExtraCameras[i].Calibration)?.Apply(extras[i]);
                inputs[c.ExtraCameras[i].Terminal] = extras[i];
            }
            long calibrationMs = Lap();

            // 4. Job — chạy đồng bộ trên luồng này; không có token nào dừng được nó. Tham số
            //    nhanh của lệnh đặt vào terminal input; record tạo ngay khi ảnh còn sống.
            JobRequest request = new(c.Outputs.Select(o => o.Terminal).ToList(),
                c.Tunables.ToDictionary(t => t.Terminal, t => t.Value, StringComparer.Ordinal),
                CreateDisplayRecords, inputs.Count == 0 ? null : inputs);
            JobRun run = product.Job(c.Job).Run(image, request);
            frame = new DisplayFrame(image, run.Record, extras);
            long runMs = Lap();

            // 5. Kết luận.
            Judgement judgement = ResultJudge.Judge(c, run);
            return new CommandResult(c.Code, judgement.Status, judgement.Error, judgement.Outputs,
                new StepTimes(queueMs, lightMs, grabMs, calibrationMs, runMs,
                    total.ElapsedMilliseconds, attempts)) { Frame = frame };
        }
        catch
        {
            if (frame is null)
            {
                image.Dispose();
                foreach (CameraImage e in extras)
                {
                    e.Dispose();
                }
            }
            else
            {
                frame.Dispose();
            }
            throw;
        }
    }

    /// <summary>Chụp một camera, chụp lại tới <c>Retry</c> lần khi lỗi lẻ.</summary>
    private async Task<CameraImage> GrabWithRetryAsync(CommandConfig c, string camera,
        CaptureSettings settings, Action<int> counted, CancellationToken ct)
    {
        int attempts = 0;
        while (true)
        {
            attempts++;
            try
            {
                CameraImage image = await _devices.GrabAsync(camera, settings, ct)
                    .ConfigureAwait(false);
                counted(attempts);
                return image;
            }
            catch (DeviceException ex) when (ex.Transient && attempts <= c.Retry)
            {
                _logger.LogWarning("Command {Code}: grab {Camera} attempt {Attempt} failed, " +
                                   "retrying: {Error}", c.Code, camera, attempts,
                    FormatForLog(ex.Info));
            }
            catch (DeviceException ex)
            {
                counted(attempts);
                throw new GrabFailedException(camera, ex.Info);
            }
        }
    }

    /// <summary>Chụp hỏng hẳn — mang theo camera nào để câu trả lời nói đúng camera.</summary>
    private sealed class GrabFailedException : Exception
    {
        public GrabFailedException(string camera, Msg info)
            : base(camera)
        {
            Camera = camera;
            Info = info;
        }

        public string Camera { get; }

        public Msg Info { get; }
    }

    private CommandResult Finish(Trace trace, CommandResult result)
    {
        result = result with
        {
            Channel = trace.Channel, Serial = trace.Serial, ReceivedAt = trace.ReceivedAt,
            ProductId = trace.ProductId, ProductVersion = trace.ProductVersion,
        };
        StepTimes t = result.Times;
        _logger.Log(result.Status == CommandStatus.Error ? LogLevel.Warning : LogLevel.Information,
            "Command {Code} {Status} total={Total} queue={Queue} light={Light} grab={Grab}x{Attempts}" +
            " calib={Calib} run={Run} {Outputs}{Error}", result.Code, result.Status.ToString(),
            t.TotalMs, t.QueueMs, t.LightMs, t.GrabMs, t.GrabAttempts, t.CalibrationMs, t.RunMs,
            string.Join(";", result.Outputs.Select(o => o.Name + "=" + o.Text)),
            result.Error is null ? string.Empty : " " + FormatForLog(result.Error));
        try
        {
            Completed?.Invoke(this, new CommandResultEventArgs(result));
        }
        catch (Exception ex)
        {
            // Người nghe (giao diện, lưu kết quả) lỗi không được làm mất câu trả lời cho máy.
            _logger.LogError(ex, "Completed handler failed for command {Code}", result.Code);
        }
        // Bộ máy chạy lệnh trả bản khung của nó: ai cần giữ ảnh đã Share trong lúc xử lý sự kiện.
        result.Frame?.Dispose();
        return result;
    }

    /// <summary>Thông tin truy vết gắn vào mọi kết quả, kể cả kết quả lỗi (Chương 23).</summary>
    private sealed record Trace(string Channel, string Serial, DateTime ReceivedAt,
        string ProductId, string ProductVersion);

    private static StepTimes Times(Stopwatch total, long queueMs = 0, long lightMs = 0) =>
        new(queueMs, lightMs, 0, 0, 0, total.ElapsedMilliseconds, 0);
}

/// <summary>Sự kiện một lệnh đã xong.</summary>
public sealed class CommandResultEventArgs : EventArgs
{
    public CommandResultEventArgs(CommandResult result) => Result = result;

    public CommandResult Result { get; }
}
