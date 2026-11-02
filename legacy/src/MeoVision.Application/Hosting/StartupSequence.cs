using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MeoVision.Core;
using Microsoft.Extensions.Logging;

namespace MeoVision.Application.Hosting;

public enum StepStatus
{
    Ok,
    Warning,
    Failed,
    Skipped,
}

/// <summary>Kết quả một bước do chính bước đó tự báo.</summary>
public sealed record StepOutcome(StepStatus Status, string Message)
{
    public static StepOutcome Ok(string message = "") => new(StepStatus.Ok, message);
    public static StepOutcome Warning(string message) => new(StepStatus.Warning, message);
    public static StepOutcome Failed(string message) => new(StepStatus.Failed, message);
}

/// <summary>Một dòng của báo cáo khởi động.</summary>
public sealed record StepResult(
    string Name, bool IsCritical, StepStatus Status, string Message, long ElapsedMs);

/// <summary>Báo cáo khởi động — hiển thị cho người vận hành và ghi vào log.</summary>
public sealed class StartupReport
{
    public StartupReport(IReadOnlyList<StepResult> steps) => Steps = steps;

    public IReadOnlyList<StepResult> Steps { get; }

    /// <summary>Chỉ được chạy tự động khi KHÔNG có bước then chốt nào hỏng hoặc bị bỏ qua.</summary>
    public bool CanRunProduction => !Steps.Any(s =>
        s.IsCritical && (s.Status == StepStatus.Failed || s.Status == StepStatus.Skipped));

    public long TotalMs => Steps.Sum(s => s.ElapsedMs);

    public string ToText()
    {
        StringBuilder sb = new();
        foreach (StepResult s in Steps)
        {
            sb.AppendLine($"[{s.Status,-7}] {s.Name,-34} {s.ElapsedMs,6} ms  {s.Message}");
        }
        sb.Append(CanRunProduction ? "=> SAN SANG CHAY TU DONG" : "=> KHONG DUOC CHAY TU DONG");
        return sb.ToString();
    }
}

/// <summary>
/// Khởi động có thứ tự (Chương 17, mục 17.4). Mỗi bước có tên, timeout riêng và cờ "then chốt":
/// bước then chốt hỏng thì các bước sau bị bỏ qua và trạm không được chạy tự động; bước không then
/// chốt hỏng chỉ thành cảnh báo (ví dụ: một camera phụ chưa cắm).
/// </summary>
public sealed class StartupSequence
{
    private readonly List<(string Name, bool IsCritical, TimeSpan Timeout,
        Func<CancellationToken, Task<StepOutcome>> Run)> _steps = new();
    private readonly ILogger _logger;

    public StartupSequence(ILogger logger)
    {
        Guard.ThrowIfNull(logger, nameof(logger));
        _logger = logger;
    }

    public StartupSequence Add(string name, bool isCritical, TimeSpan timeout,
        Func<CancellationToken, Task<StepOutcome>> run)
    {
        Guard.ThrowIfNull(name, nameof(name));
        Guard.ThrowIfNull(run, nameof(run));
        _steps.Add((name, isCritical, timeout, run));
        return this;
    }

    public async Task<StartupReport> RunAsync(CancellationToken ct = default)
    {
        List<StepResult> results = new();
        string? stoppedBy = null;

        foreach ((string name, bool isCritical, TimeSpan timeout,
                     Func<CancellationToken, Task<StepOutcome>> run) in _steps)
        {
            if (stoppedBy is not null)
            {
                results.Add(new StepResult(name, isCritical, StepStatus.Skipped,
                    $"bo qua vi '{stoppedBy}' hong", 0));
                continue;
            }

            Stopwatch sw = Stopwatch.StartNew();
            StepOutcome outcome = await RunOneAsync(run, timeout, ct).ConfigureAwait(false);
            sw.Stop();

            // Bước KHÔNG then chốt mà hỏng thì hạ thành cảnh báo: trạm vẫn lên, nhưng có ghi lại.
            if (outcome.Status == StepStatus.Failed && !isCritical)
            {
                outcome = StepOutcome.Warning(outcome.Message);
            }

            results.Add(new StepResult(name, isCritical, outcome.Status, outcome.Message,
                sw.ElapsedMilliseconds));
            Log(name, outcome, sw.ElapsedMilliseconds);

            if (outcome.Status == StepStatus.Failed)
            {
                stoppedBy = name;
            }
        }

        StartupReport report = new(results);
        _logger.LogInformation("Khoi dong xong sau {Ms} ms:{NewLine}{Report}",
            report.TotalMs, Environment.NewLine, report.ToText());
        return report;
    }

    private async Task<StepOutcome> RunOneAsync(
        Func<CancellationToken, Task<StepOutcome>> run, TimeSpan timeout, CancellationToken ct)
    {
        using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        // Task.Run: kể cả bước gọi SDK đồng bộ (không nghe token) vẫn không chặn được cả chuỗi.
        Task<StepOutcome> work = Task.Run(() => run(cts.Token), cts.Token);
        Task finished = await Task.WhenAny(work, Task.Delay(timeout, ct)).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();

        if (finished != work)
        {
            cts.Cancel();
            // Bước vẫn có thể đang chạy ngầm: quan sát lỗi để không thành UnobservedTaskException.
            _ = work.ContinueWith(t => _logger.LogWarning(t.Exception,
                    "Buoc qua thoi gian ket thuc muon voi loi"),
                TaskContinuationOptions.OnlyOnFaulted);
            return StepOutcome.Failed($"qua thoi gian {timeout.TotalMilliseconds:0} ms");
        }

        try
        {
            return await work.ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "Buoc khoi dong nem loi");
            return StepOutcome.Failed($"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private void Log(string name, StepOutcome outcome, long ms)
    {
        LogLevel level = outcome.Status switch
        {
            StepStatus.Ok => LogLevel.Information,
            StepStatus.Warning => LogLevel.Warning,
            _ => LogLevel.Error,
        };
        _logger.Log(level, "[Khoi dong] {Step}: {Status} ({Ms} ms) {Message}",
            name, outcome.Status, ms, outcome.Message);
    }
}
