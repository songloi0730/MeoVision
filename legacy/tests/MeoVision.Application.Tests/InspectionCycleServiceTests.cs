using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MeoVision.Application.Alarms;
using MeoVision.Application.Archiving;
using MeoVision.Application.Recipes;
using MeoVision.Application.Results;
using MeoVision.Core.Contracts;
using MeoVision.Core.Exceptions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace MeoVision.Application.Tests;

public class InspectionCycleServiceTests
{
    private static readonly VisionRecipe Recipe = new(
        RecipeId: "test",
        JobName: "TB_Test",
        JobFilePath: "x.vpp",
        SampleImageFolder: "x",
        MinAcceptScore: 0.75,
        PartWidthMinMm: 24.5,
        PartWidthMaxMm: 25.5,
        ExpectedPadCount: 6,
        MaxRetries: 2,
        CycleTimeoutMs: 2000,
        JobLoadTimeoutMs: 10_000);

    private static VisionResult MakeResult(
        VisionOutcome outcome, VisionFailureReason reason = VisionFailureReason.None, double score = 0.9)
    {
        return new VisionResult(
            CorrelationId: Guid.NewGuid(),
            JobName: Recipe.JobName,
            Outcome: outcome,
            FailureReason: reason,
            Score: score,
            X: 0,
            Y: 0,
            AngleDeg: 0,
            Code: null,
            Checks: Array.Empty<VisionCheckResult>(),
            TimestampUtc: DateTimeOffset.UtcNow,
            ProcessingTimeMs: 10,
            Image: new Bitmap(4, 4));
    }

    private static (
        InspectionCycleService Service,
        Mock<IVisionEngine> Engine,
        Mock<IInspectionResultStore> ResultStore,
        Mock<IAlarmService> AlarmService,
        Mock<INgImageArchiver> NgImageArchiver) CreateService()
    {
        Mock<IVisionEngine> engine = new();
        Mock<IRecipeRepository> recipeRepository = new();
        Mock<IInspectionResultStore> resultStore = new();
        Mock<IAlarmService> alarmService = new();
        Mock<INgImageArchiver> ngImageArchiver = new();

        recipeRepository
            .Setup(r => r.GetAsync(Recipe.RecipeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Recipe);
        alarmService
            .Setup(a => a.RaiseAsync(
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<AlarmSeverity>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        InspectionCycleService service = new(
            engine.Object,
            recipeRepository.Object,
            resultStore.Object,
            alarmService.Object,
            ngImageArchiver.Object,
            NullLogger<InspectionCycleService>.Instance);

        return (service, engine, resultStore, alarmService, ngImageArchiver);
    }

    [Fact]
    public async Task RunCycleAsync_Should_StoreResult_WhenEngineAccepts()
    {
        (InspectionCycleService service, Mock<IVisionEngine> engine, Mock<IInspectionResultStore> resultStore, _, _) =
            CreateService();
        engine
            .Setup(e => e.InspectAsync(It.IsAny<VisionJobRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeResult(VisionOutcome.Accept));

        await service.LoadActiveRecipeAsync(Recipe.RecipeId);
        VisionResult result = await service.RunCycleAsync();

        result.Outcome.Should().Be(VisionOutcome.Accept);
        resultStore.Verify(
            s => s.AddAsync(
                It.Is<InspectionLogEntry>(e => e.Outcome == VisionOutcome.Accept), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RunCycleAsync_Should_RetryUpToMaxRetries_WhenEngineReturnsPartNotFound()
    {
        (InspectionCycleService service, Mock<IVisionEngine> engine, _, _, _) = CreateService();
        engine
            .SetupSequence(e => e.InspectAsync(It.IsAny<VisionJobRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeResult(VisionOutcome.Reject, VisionFailureReason.PartNotFound))
            .ReturnsAsync(MakeResult(VisionOutcome.Reject, VisionFailureReason.PartNotFound))
            .ReturnsAsync(MakeResult(VisionOutcome.Accept));

        await service.LoadActiveRecipeAsync(Recipe.RecipeId);
        VisionResult result = await service.RunCycleAsync();

        result.Outcome.Should().Be(VisionOutcome.Accept);
        engine.Verify(
            e => e.InspectAsync(It.IsAny<VisionJobRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task RunCycleAsync_Should_NotRetry_WhenFailureReasonIsMeasurementFailed()
    {
        (InspectionCycleService service, Mock<IVisionEngine> engine, _, _, _) = CreateService();
        engine
            .Setup(e => e.InspectAsync(It.IsAny<VisionJobRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeResult(VisionOutcome.Reject, VisionFailureReason.MeasurementFailed));

        await service.LoadActiveRecipeAsync(Recipe.RecipeId);
        await service.RunCycleAsync();

        engine.Verify(
            e => e.InspectAsync(It.IsAny<VisionJobRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(VisionOutcome.Accept, false)]
    [InlineData(VisionOutcome.Reject, true)]
    [InlineData(VisionOutcome.Warning, true)]
    public async Task RunCycleAsync_Should_ArchiveImage_OnlyWhen_OutcomeIsRejectOrWarning(
        VisionOutcome outcome, bool expectArchive)
    {
        (InspectionCycleService service, Mock<IVisionEngine> engine, _, _, Mock<INgImageArchiver> ngImageArchiver) =
            CreateService();
        VisionFailureReason reason = outcome == VisionOutcome.Accept
            ? VisionFailureReason.None
            : VisionFailureReason.MeasurementFailed;
        engine
            .Setup(e => e.InspectAsync(It.IsAny<VisionJobRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeResult(outcome, reason));

        await service.LoadActiveRecipeAsync(Recipe.RecipeId);
        await service.RunCycleAsync();

        ngImageArchiver.Verify(
            a => a.ArchiveIfNeededAsync(
                It.IsAny<Bitmap>(), It.IsAny<VisionOutcome>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            expectArchive ? Times.Once() : Times.Never());
    }

    [Fact]
    public async Task RunCycleAsync_Should_RaiseAlarm_WhenEngineThrowsVisionEngineTimeoutException()
    {
        (InspectionCycleService service, Mock<IVisionEngine> engine, _, Mock<IAlarmService> alarmService, _) =
            CreateService();
        engine
            .Setup(e => e.InspectAsync(It.IsAny<VisionJobRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new VisionEngineTimeoutException("timeout", Recipe.JobName));

        await service.LoadActiveRecipeAsync(Recipe.RecipeId);

        Func<Task> act = () => service.RunCycleAsync();

        await act.Should().ThrowAsync<VisionEngineTimeoutException>();
        alarmService.Verify(
            a => a.RaiseAsync(
                AlarmCodes.InspectionTimeout, It.IsAny<string>(), AlarmSeverity.Warning, It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
