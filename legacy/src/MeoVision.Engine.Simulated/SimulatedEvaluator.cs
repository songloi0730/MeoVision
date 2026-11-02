using System;
using System.Collections.Generic;
using MeoVision.Core;
using MeoVision.Core.Contracts;

namespace MeoVision.Engine.Simulated;

internal readonly record struct SimulatedReading(
    double MeasuredWidthMm,
    double AngleDeg,
    double OffsetXPx,
    double OffsetYPx,
    int PadCount,
    bool DefectInjected,
    double ScoreBase);

// Tách riêng khỏi SimulatedVisionEngine để phần đánh giá (so khớp recipe → Outcome/Score/Checks)
// là hàm THUẦN, TẤT ĐỊNH — test được trực tiếp không cần điều khiển Random bên trong engine.
internal static class SimulatedEvaluator
{
    public static (VisionOutcome Outcome, VisionFailureReason Reason, double Score, List<VisionCheckResult> Checks)
        Evaluate(SimulatedReading reading, VisionRecipe recipe)
    {
        Guard.ThrowIfNull(recipe, nameof(recipe));

        bool widthOk = reading.MeasuredWidthMm >= recipe.PartWidthMinMm
            && reading.MeasuredWidthMm <= recipe.PartWidthMaxMm;
        bool padCountOk = reading.PadCount == recipe.ExpectedPadCount;

        double score = reading.ScoreBase;
        if (!widthOk)
        {
            score -= 0.35;
        }

        if (!padCountOk)
        {
            score -= 0.30;
        }

        if (reading.DefectInjected)
        {
            score -= 0.40;
        }

        score = Math.Max(0.0, Math.Min(1.0, score));

        List<VisionCheckResult> checks = new()
        {
            new VisionCheckResult(
                "PartWidthMm", widthOk, reading.MeasuredWidthMm, recipe.PartWidthMinMm, recipe.PartWidthMaxMm),
            new VisionCheckResult(
                "PadCount", padCountOk, reading.PadCount, recipe.ExpectedPadCount, recipe.ExpectedPadCount),
            new VisionCheckResult(
                "SurfaceDefect", !reading.DefectInjected, reading.DefectInjected ? 1 : 0, null, 0)
        };

        VisionOutcome outcome;
        VisionFailureReason reason;
        if (reading.PadCount <= 0)
        {
            outcome = VisionOutcome.Reject;
            reason = VisionFailureReason.PartNotFound;
        }
        else if (!widthOk || !padCountOk || reading.DefectInjected)
        {
            outcome = score >= recipe.MinAcceptScore ? VisionOutcome.Warning : VisionOutcome.Reject;
            reason = VisionFailureReason.MeasurementFailed;
        }
        else
        {
            outcome = VisionOutcome.Accept;
            reason = VisionFailureReason.None;
        }

        return (outcome, reason, score, checks);
    }
}
