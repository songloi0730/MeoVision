using FluentAssertions;
using MeoVision.Core.Contracts;
using Xunit;

namespace MeoVision.Engine.Simulated.Tests;

public class SimulatedEvaluatorTests
{
    private static readonly VisionRecipe Recipe = TestRecipes.Default(@"C:\");

    [Fact]
    public void Evaluate_Should_ReturnAccept_WhenAllMeasurementsWithinRecipeThresholds()
    {
        SimulatedReading reading = new(
            MeasuredWidthMm: 25.0, AngleDeg: 0, OffsetXPx: 0, OffsetYPx: 0,
            PadCount: Recipe.ExpectedPadCount, DefectInjected: false, ScoreBase: 0.95);

        (VisionOutcome outcome, VisionFailureReason reason, double score, var checks) =
            SimulatedEvaluator.Evaluate(reading, Recipe);

        outcome.Should().Be(VisionOutcome.Accept);
        reason.Should().Be(VisionFailureReason.None);
        score.Should().BeApproximately(0.95, 0.001);
        checks.Should().OnlyContain(c => c.Passed);
    }

    [Fact]
    public void Evaluate_Should_ReturnFailure_WhenPadCountMismatch()
    {
        SimulatedReading reading = new(
            MeasuredWidthMm: 25.0, AngleDeg: 0, OffsetXPx: 0, OffsetYPx: 0,
            PadCount: Recipe.ExpectedPadCount - 1, DefectInjected: false, ScoreBase: 0.90);

        (VisionOutcome outcome, VisionFailureReason reason, _, var checks) =
            SimulatedEvaluator.Evaluate(reading, Recipe);

        outcome.Should().BeOneOf(VisionOutcome.Reject, VisionOutcome.Warning);
        reason.Should().Be(VisionFailureReason.MeasurementFailed);
        checks.Should().Contain(c => c.Name == "PadCount" && !c.Passed);
    }

    [Fact]
    public void Evaluate_Should_ReturnPartNotFound_WhenPadCountIsZero()
    {
        SimulatedReading reading = new(
            MeasuredWidthMm: 25.0, AngleDeg: 0, OffsetXPx: 0, OffsetYPx: 0,
            PadCount: 0, DefectInjected: false, ScoreBase: 0.90);

        (VisionOutcome outcome, VisionFailureReason reason, _, _) =
            SimulatedEvaluator.Evaluate(reading, Recipe);

        outcome.Should().Be(VisionOutcome.Reject);
        reason.Should().Be(VisionFailureReason.PartNotFound);
    }

    [Fact]
    public void Evaluate_Should_ReturnReject_WhenWidthFarOutsideTolerance()
    {
        SimulatedReading reading = new(
            MeasuredWidthMm: 30.0, AngleDeg: 0, OffsetXPx: 0, OffsetYPx: 0,
            PadCount: Recipe.ExpectedPadCount, DefectInjected: false, ScoreBase: 0.90);

        (VisionOutcome outcome, VisionFailureReason reason, double score, var checks) =
            SimulatedEvaluator.Evaluate(reading, Recipe);

        outcome.Should().Be(VisionOutcome.Reject);
        reason.Should().Be(VisionFailureReason.MeasurementFailed);
        score.Should().BeLessThan((double)Recipe.MinAcceptScore);
        checks.Should().Contain(c => c.Name == "PartWidthMm" && !c.Passed);
    }
}
