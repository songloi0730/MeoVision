using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using MeoVision.Core;
using Microsoft.Extensions.Logging;

namespace MeoVision.Application.Hosting;

/// <summary>
/// Tắt máy có thứ tự (Chương 17, mục 17.7): ngược chiều khởi động — ngừng nhận lệnh trước, chờ
/// chu kỳ đang chạy, lưu bộ đếm, rồi mới trả phần cứng. Khác khởi động ở một điểm: một bước hỏng
/// KHÔNG được chặn các bước sau (đèn chưa tắt còn hơn camera không được ngắt).
/// </summary>
public sealed class ShutdownSequence
{
    private readonly List<(string Name, TimeSpan Timeout, Func<Task> Run)> _steps = new();
    private readonly ILogger _logger;

    public ShutdownSequence(ILogger logger)
    {
        Guard.ThrowIfNull(logger, nameof(logger));
        _logger = logger;
    }

    public ShutdownSequence Add(string name, TimeSpan timeout, Func<Task> run)
    {
        Guard.ThrowIfNull(name, nameof(name));
        Guard.ThrowIfNull(run, nameof(run));
        _steps.Add((name, timeout, run));
        return this;
    }

    public ShutdownSequence Add(string name, TimeSpan timeout, Action run)
    {
        Guard.ThrowIfNull(run, nameof(run));
        return Add(name, timeout, () => Task.Run(run));
    }

    public async Task<IReadOnlyList<StepResult>> RunAsync()
    {
        List<StepResult> results = new();
        foreach ((string name, TimeSpan timeout, Func<Task> run) in _steps)
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
                        "Buoc tat may ket thuc muon voi loi"), TaskContinuationOptions.OnlyOnFaulted);
                    outcome = StepOutcome.Failed($"qua thoi gian {timeout.TotalMilliseconds:0} ms");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Tat may] {Step} nem loi", name);
                outcome = StepOutcome.Failed($"{ex.GetType().Name}: {ex.Message}");
            }

            sw.Stop();
            results.Add(new StepResult(name, false, outcome.Status, outcome.Message,
                sw.ElapsedMilliseconds));
            _logger.Log(outcome.Status == StepStatus.Ok ? LogLevel.Information : LogLevel.Error,
                "[Tat may] {Step}: {Status} ({Ms} ms) {Message}",
                name, outcome.Status, sw.ElapsedMilliseconds, outcome.Message);
        }
        return results;
    }
}
