using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MeoVision.Core;
using MeoVision.Core.Contracts;
using MeoVision.Core.Exceptions;
using Microsoft.Extensions.Logging;

namespace MeoVision.Engine.Simulated;

public sealed class SimulatedVisionEngine : IVisionEngine
{
    private readonly ILogger<SimulatedVisionEngine> _logger;
    private readonly object _randomLock = new();
    private readonly Random _random;
    private VisionRecipe? _recipe;

    // random=null (mặc định khi DI resolve) dùng Random() thật; test tự truyền seed cố định để
    // lặp lại được kết quả — đây là lý do duy nhất tham số này tồn tại, không phải để dùng thường.
    public SimulatedVisionEngine(ILogger<SimulatedVisionEngine> logger, Random? random = null)
    {
        Guard.ThrowIfNull(logger, nameof(logger));
        _logger = logger;
        _random = random ?? new Random();
    }

    public string EngineName => "Simulated";

    public bool IsJobLoaded => _recipe is not null;

    public event EventHandler<VisionResult>? InspectionCompleted;

    public Task LoadRecipeAsync(VisionRecipe recipe, CancellationToken ct = default)
    {
        Guard.ThrowIfNull(recipe, nameof(recipe));
        ct.ThrowIfCancellationRequested();

        // Bản giả lập không đọc ảnh thật trong SampleImageFolder (vẽ thủ tục bằng
        // SyntheticPartRenderer), nhưng vẫn xác nhận thư mục tồn tại — recipe sai đường dẫn cấu
        // hình cần lộ ra ngay lúc nạp, giống cách bản Engine thật sẽ báo lỗi khi JobFilePath sai.
        if (!Directory.Exists(recipe.SampleImageFolder))
        {
            throw new VisionRecipeException(
                $"SampleImageFolder không tồn tại: {recipe.SampleImageFolder}", recipe.JobName);
        }

        _recipe = recipe;
        _logger.LogInformation("Đã nạp recipe {RecipeId} (job={JobName}) vào Engine giả lập",
            recipe.RecipeId, recipe.JobName);
        return Task.CompletedTask;
    }

    public Task TeachPatternAsync(VisionTrainRegion trainRegion, CancellationToken ct = default)
    {
        Guard.ThrowIfNull(trainRegion, nameof(trainRegion));
        ct.ThrowIfCancellationRequested();
        _logger.LogInformation("Giả lập ghi nhận teach pattern '{PatternName}' (không lưu xuống đĩa)",
            trainRegion.PatternName);
        return Task.CompletedTask;
    }

    public async Task<VisionResult> InspectAsync(VisionJobRequest request, CancellationToken ct = default)
    {
        Guard.ThrowIfNull(request, nameof(request));

        VisionRecipe recipe = _recipe
            ?? throw new VisionEngineException("Chưa nạp recipe — gọi LoadRecipeAsync trước.");

        using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(recipe.CycleTimeoutMs);

        try
        {
            int simulatedLatencyMs = NextInt(40, 130);
            await Task.Delay(simulatedLatencyMs, cts.Token).ConfigureAwait(false);
            return RunSimulatedCycle(request, recipe, simulatedLatencyMs);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new VisionEngineTimeoutException(
                $"Chu trình giả lập vượt quá {recipe.CycleTimeoutMs}ms", recipe.JobName);
        }
    }

    private VisionResult RunSimulatedCycle(VisionJobRequest request, VisionRecipe recipe, int processingTimeMs)
    {
        SimulatedReading reading = GenerateReading(recipe);
        (VisionOutcome outcome, VisionFailureReason reason, double score, var checks) =
            SimulatedEvaluator.Evaluate(reading, recipe);

        Bitmap resultImage = SyntheticPartRenderer.Render(
            widthMm: reading.MeasuredWidthMm,
            heightMm: Math.Max((recipe.PartWidthMinMm + recipe.PartWidthMaxMm) / 2.0 * 0.4, 5.0),
            offsetXPx: reading.OffsetXPx,
            offsetYPx: reading.OffsetYPx,
            angleDeg: reading.AngleDeg,
            padCount: Math.Max(reading.PadCount, 0),
            injectDefectBlob: reading.DefectInjected);

        VisionResult result = new(
            CorrelationId: request.CorrelationId,
            JobName: recipe.JobName,
            Outcome: outcome,
            FailureReason: reason,
            Score: score,
            X: reading.OffsetXPx,
            Y: reading.OffsetYPx,
            AngleDeg: reading.AngleDeg,
            Code: null,
            Checks: checks,
            TimestampUtc: DateTimeOffset.UtcNow,
            ProcessingTimeMs: processingTimeMs,
            Image: resultImage);

        _logger.LogInformation(
            "Giả lập kiểm tra {CorrelationId} outcome={Outcome} score={Score:F3} width={Width:F2}mm pad={Pad}",
            result.CorrelationId, result.Outcome, result.Score, reading.MeasuredWidthMm, reading.PadCount);

        InspectionCompleted?.Invoke(this, result);
        return result;
    }

    private SimulatedReading GenerateReading(VisionRecipe recipe)
    {
        double nominalWidthMm = (recipe.PartWidthMinMm + recipe.PartWidthMaxMm) / 2.0;
        double toleranceMm = (recipe.PartWidthMaxMm - recipe.PartWidthMinMm) / 2.0;

        // Jitter đủ rộng để thỉnh thoảng vượt ngoài dải dung sai (2.4x) — nếu chỉ jitter TRONG
        // dải, mọi lần chạy đều Accept và bài test "không hardcode giá trị trả về" (skill
        // meovision-sim-parity) sẽ vô nghĩa vì không có gì để phân biệt Accept/Reject thật sự.
        double widthNoiseMm = (NextDouble() - 0.5) * toleranceMm * 2.4;
        double angleDeg = (NextDouble() - 0.5) * 6.0;
        double offsetXPx = (NextDouble() - 0.5) * 20.0;
        double offsetYPx = (NextDouble() - 0.5) * 20.0;
        bool defectInjected = NextDouble() < 0.12;
        int padCount = NextDouble() < 0.08
            ? recipe.ExpectedPadCount + (NextDouble() < 0.5 ? -1 : 1)
            : recipe.ExpectedPadCount;
        double scoreBase = 0.90 + (NextDouble() * 0.09);

        return new SimulatedReading(
            nominalWidthMm + widthNoiseMm, angleDeg, offsetXPx, offsetYPx, padCount, defectInjected, scoreBase);
    }

    private double NextDouble()
    {
        lock (_randomLock)
        {
            return _random.NextDouble();
        }
    }

    private int NextInt(int minInclusive, int maxExclusive)
    {
        lock (_randomLock)
        {
            return _random.Next(minInclusive, maxExclusive);
        }
    }

    public void Dispose()
    {
        // Không có tài nguyên native/unmanaged nào cần giải phóng ở bản giả lập — method này tồn
        // tại để khớp IVisionEngine (bản Cognex thật giải phóng CogToolBlock ở đây, xem
        // MeoVision.Engine.CognexVisionEngine).
    }
}
