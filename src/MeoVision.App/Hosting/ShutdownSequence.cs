using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using MeoVision.Core;
using MeoVision.Core.Localization;
using Microsoft.Extensions.Logging;

namespace MeoVision.App.Hosting;

/// <summary>
/// Tắt máy có thứ tự (Chương 17, mục 17.7): ngược chiều khởi động — ngừng nhận lệnh trước,
/// chờ chu kỳ đang chạy, lưu bộ đếm, rồi mới trả phần cứng. Khác khởi động ở một điểm: một
/// bước hỏng KHÔNG được chặn các bước sau (đèn chưa tắt còn hơn camera không được ngắt).
/// </summary>
public sealed class ShutdownSequence
{
    private readonly List<(string NameKey, TimeSpan Timeout, Func<Task> Run)> _steps = new();
    private readonly ILogger _logger;
    private readonly ILocalizer _logText;

    public ShutdownSequence(ILogger logger, ILocalizer logText)
    {
        Guard.ThrowIfNull(logger, nameof(logger));
        Guard.ThrowIfNull(logText, nameof(logText));
        _logger = logger;
        _logText = logText;
    }

    public ShutdownSequence Add(string nameKey, TimeSpan timeout, Func<Task> run)
    {
        Guard.ThrowIfNull(nameKey, nameof(nameKey));
        Guard.ThrowIfNull(run, nameof(run));
        _steps.Add((nameKey, timeout, run));
        return this;
    }

    public ShutdownSequence Add(string nameKey, TimeSpan timeout, Action run)
    {
        Guard.ThrowIfNull(run, nameof(run));
        return Add(nameKey, timeout, () => Task.Run(run));
    }

    public async Task<IReadOnlyList<StepResult>> RunAsync()
    {
        List<StepResult> results = new();
        foreach ((string nameKey, TimeSpan timeout, Func<Task> run) in _steps)
        {
            Stopwatch sw = Stopwatch.StartNew();
            StepOutcome outcome;
            try
            {
                Task work = Task.Run(run);
                Task finished = await Task.WhenAny(work, Task.Delay(timeout))
                    .ConfigureAwait(false);
                if (finished == work)
                {
                    await work.ConfigureAwait(false);   // lấy exception nếu có
                    outcome = StepOutcome.Ok();
                }
                else
                {
                    _ = work.ContinueWith(t => _logger.LogWarning(t.Exception,
                            "Timed-out shutdown step finished later with an error"),
                        TaskContinuationOptions.OnlyOnFaulted);
                    outcome = StepOutcome.Failed(
                        Msg.Of("step.timeout", timeout.TotalMilliseconds));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Shutdown step {Step} threw", _logText.Text(nameKey));
                outcome = StepOutcome.Failed(Msg.Of("step.exception", ex.GetType().Name,
                    ex.Message));
            }

            sw.Stop();
            results.Add(new StepResult(nameKey, false, outcome.Status, outcome.Message,
                sw.ElapsedMilliseconds));
            LogLevel level = outcome.Status == StepStatus.Ok
                ? LogLevel.Information : LogLevel.Error;
            _logger.Log(level, "[Shutdown] {Step}: {Status} ({Ms} ms) {Message}",
                _logText.Text(nameKey), outcome.Status.ToString(), sw.ElapsedMilliseconds,
                _logText.Text(outcome.Message));
        }
        return results;
    }
}
