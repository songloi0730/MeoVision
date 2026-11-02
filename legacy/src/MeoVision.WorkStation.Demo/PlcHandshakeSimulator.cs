using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using MeoVision.Application;
using MeoVision.Core;
using MeoVision.Core.Contracts;
using Microsoft.Extensions.Logging;

namespace MeoVision.WorkStation.Demo;

// Orchestrator: gọi IPlcCommunication cho ĐÚNG 4 tín hiệu thô (bản giả lập tự in ra console — xem
// SimulatedPlcCommunication), còn narrate chi tiết Outcome/Score là việc riêng của lớp này (nghiệp
// vụ vision, không phải ngữ nghĩa của giao thức PLC). Đặt watchdog RIÊNG cho "phía PLC" (rộng hơn
// CycleTimeoutMs nội bộ của Vision) — đúng nguyên tắc Ch15 §15.1.3: mỗi bên tự có timeout độc lập.
public sealed class PlcHandshakeSimulator
{
    private readonly IPlcCommunication _plc;
    private readonly InspectionCycleService _cycleService;
    private readonly ILogger<PlcHandshakeSimulator> _logger;

    public PlcHandshakeSimulator(
        IPlcCommunication plc, InspectionCycleService cycleService, ILogger<PlcHandshakeSimulator> logger)
    {
        Guard.ThrowIfNull(plc, nameof(plc));
        Guard.ThrowIfNull(cycleService, nameof(cycleService));
        Guard.ThrowIfNull(logger, nameof(logger));
        _plc = plc;
        _cycleService = cycleService;
        _logger = logger;
    }

    public async Task RunAsync(int cycleCount, CancellationToken ct = default)
    {
        VisionRecipe recipe = _cycleService.ActiveRecipe
            ?? throw new InvalidOperationException("Chưa nạp recipe — gọi LoadActiveRecipeAsync trước.");

        // Công thức Ch15 §15.5.2: retry làm worst-case một chu trình tăng tuyến tính theo
        // (maxRetries+1) — PLC phải biết số này để đặt watchdog đúng, không phải đoán.
        int worstCaseWithRetryMs = (recipe.MaxRetries + 1) * recipe.CycleTimeoutMs;
        int plcWatchdogMs = worstCaseWithRetryMs + 2000;

        Console.WriteLine(
            $"Worst-case 1 chu trình có retry: ({recipe.MaxRetries}+1) x {recipe.CycleTimeoutMs}ms = " +
            $"{worstCaseWithRetryMs}ms (Ch15 §15.5.2). Watchdog phía PLC đặt {plcWatchdogMs}ms " +
            "(rộng hơn — 2 đồng hồ độc lập, không dùng chung, Ch15 §15.1.3).");
        Console.WriteLine();

        await _plc.ConnectAsync(ct).ConfigureAwait(false);

        int okCount = 0;
        int ngCount = 0;

        for (int cycle = 1; cycle <= cycleCount; cycle++)
        {
            ct.ThrowIfCancellationRequested();
            Console.WriteLine($"--- Chu trình {cycle}/{cycleCount} ---");

            await _plc.WaitForTriggerAsync(ct).ConfigureAwait(false);
            await _plc.SetBusyAsync(true, ct).ConfigureAwait(false);

            using CancellationTokenSource plcWatchdog = new(plcWatchdogMs);
            using CancellationTokenSource linked =
                CancellationTokenSource.CreateLinkedTokenSource(ct, plcWatchdog.Token);

            Stopwatch sw = Stopwatch.StartNew();
            VisionResult result;
            try
            {
                result = await _cycleService.RunCycleAsync(linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (plcWatchdog.IsCancellationRequested)
            {
                _logger.LogError("Watchdog PLC ({WatchdogMs}ms) hết hạn ở chu trình {Cycle}", plcWatchdogMs, cycle);
                Console.WriteLine("[PLC]    !! Watchdog hết hạn — Vision không phản hồi kịp, dừng mô phỏng !!");
                break;
            }

            sw.Stop();
            await _plc.SetBusyAsync(false, ct).ConfigureAwait(false);
            await _plc.SetDoneAsync(true, ct).ConfigureAwait(false);
            Console.WriteLine($"         Outcome={result.Outcome} Score={result.Score:F3} ({sw.ElapsedMilliseconds}ms)");

            await _plc.WaitForAckAsync(ct).ConfigureAwait(false);
            await _plc.SetDoneAsync(false, ct).ConfigureAwait(false);
            Console.WriteLine();

            if (result.Pass)
            {
                okCount++;
            }
            else
            {
                ngCount++;
            }

            await Task.Delay(600, ct).ConfigureAwait(false);
        }

        Console.WriteLine($"=== Tổng kết: {cycleCount} chu trình — OK={okCount} NG={ngCount} ===");
    }
}
