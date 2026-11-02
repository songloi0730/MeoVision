using System;
using System.Threading;
using System.Threading.Tasks;
using MeoVision.Application.Alarms;
using MeoVision.Application.Archiving;
using MeoVision.Application.Recipes;
using MeoVision.Application.Results;
using MeoVision.Core;
using MeoVision.Core.Contracts;
using MeoVision.Core.Exceptions;
using Microsoft.Extensions.Logging;

namespace MeoVision.Application;

public sealed class InspectionCycleService
{
    private readonly IVisionEngine _engine;
    private readonly IRecipeRepository _recipeRepository;
    private readonly IInspectionResultStore _resultStore;
    private readonly IAlarmService _alarmService;
    private readonly INgImageArchiver _ngImageArchiver;
    private readonly ILogger<InspectionCycleService> _logger;
    private VisionRecipe? _activeRecipe;

    public InspectionCycleService(
        IVisionEngine engine,
        IRecipeRepository recipeRepository,
        IInspectionResultStore resultStore,
        IAlarmService alarmService,
        INgImageArchiver ngImageArchiver,
        ILogger<InspectionCycleService> logger)
    {
        Guard.ThrowIfNull(engine, nameof(engine));
        Guard.ThrowIfNull(recipeRepository, nameof(recipeRepository));
        Guard.ThrowIfNull(resultStore, nameof(resultStore));
        Guard.ThrowIfNull(alarmService, nameof(alarmService));
        Guard.ThrowIfNull(ngImageArchiver, nameof(ngImageArchiver));
        Guard.ThrowIfNull(logger, nameof(logger));

        _engine = engine;
        _recipeRepository = recipeRepository;
        _resultStore = resultStore;
        _alarmService = alarmService;
        _ngImageArchiver = ngImageArchiver;
        _logger = logger;
    }

    public string? ActiveRecipeId => _activeRecipe?.RecipeId;

    public VisionRecipe? ActiveRecipe => _activeRecipe;

    public async Task LoadActiveRecipeAsync(string recipeId, CancellationToken ct = default)
    {
        Guard.ThrowIfNull(recipeId, nameof(recipeId));

        VisionRecipe recipe = await _recipeRepository.GetAsync(recipeId, ct).ConfigureAwait(false);

        try
        {
            await _engine.LoadRecipeAsync(recipe, ct).ConfigureAwait(false);
            _activeRecipe = recipe;
            _logger.LogInformation("Recipe hiện hành: {RecipeId} (job={JobName})",
                recipe.RecipeId, recipe.JobName);
        }
        catch (VisionRecipeException ex)
        {
            await _alarmService.RaiseAsync(
                AlarmCodes.RecipeInvalid, $"Recipe không hợp lệ: {ex.Message}", AlarmSeverity.Stop, ct)
                .ConfigureAwait(false);
            throw;
        }
        catch (VisionEngineException ex)
        {
            await _alarmService.RaiseAsync(
                AlarmCodes.JobLoadFailed, $"Nạp job thất bại: {ex.Message}", AlarmSeverity.Stop, ct)
                .ConfigureAwait(false);
            throw;
        }
    }

    public async Task<VisionResult> RunCycleAsync(CancellationToken ct = default)
    {
        VisionRecipe recipe = _activeRecipe
            ?? throw new VisionEngineException("Chưa nạp recipe — gọi LoadActiveRecipeAsync trước.");

        VisionJobRequest request = VisionJobRequest.CreateNew();
        int attempt = 0;

        while (true)
        {
            attempt++;
            VisionResult result;
            try
            {
                result = await _engine.InspectAsync(request, ct).ConfigureAwait(false);
            }
            catch (VisionEngineTimeoutException ex)
            {
                await _alarmService.RaiseAsync(
                    AlarmCodes.InspectionTimeout, ex.Message, AlarmSeverity.Warning, ct)
                    .ConfigureAwait(false);
                throw;
            }
            catch (VisionEngineException ex)
            {
                await _alarmService.RaiseAsync(
                    AlarmCodes.EngineFault, ex.Message, AlarmSeverity.Stop, ct).ConfigureAwait(false);
                throw;
            }

            // Chỉ retry khi lý do là KHÔNG THẤY VẬT (băng tải rung/che khuất) — retry khi
            // MeasurementFailed là VÔ NGHĨA: vật vẫn lỗi y hệt ở lần chụp sau, chỉ tốn thời gian
            // chu trình một cách vô ích (Ch15 §15.4.2's phân biệt này).
            bool shouldRetry = result.FailureReason == VisionFailureReason.PartNotFound
                && attempt <= recipe.MaxRetries;
            if (shouldRetry)
            {
                _logger.LogWarning(
                    "Không thấy vật (lần {Attempt}/{Max}), thử lại {CorrelationId}",
                    attempt, recipe.MaxRetries + 1, result.CorrelationId);
                continue;
            }

            await FinalizeCycleAsync(result, attempt, ct).ConfigureAwait(false);
            return result;
        }
    }

    private async Task FinalizeCycleAsync(VisionResult result, int attemptCount, CancellationToken ct)
    {
        // Policy "chỉ lưu NG/Warning" (Ch14 §14.5.1) sống ở đây — INgImageArchiver chỉ lo cơ chế
        // lưu + retention, không tự quyết định outcome nào đáng lưu.
        bool shouldArchive = result.Outcome is VisionOutcome.Reject or VisionOutcome.Warning or VisionOutcome.Error;

        string? savedImagePath = null;
        if (shouldArchive && result.Image is not null)
        {
            savedImagePath = await _ngImageArchiver
                .ArchiveIfNeededAsync(result.Image, result.Outcome, result.CorrelationId.ToString(), ct)
                .ConfigureAwait(false);
        }

        InspectionLogEntry entry = new(
            result.CorrelationId, result.JobName, result.Outcome, result.FailureReason,
            result.Score, result.TimestampUtc, attemptCount, savedImagePath, result.ProcessingTimeMs);
        await _resultStore.AddAsync(entry, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Chu trình xong {CorrelationId} attempt={Attempt} outcome={Outcome} score={Score:F3}",
            result.CorrelationId, attemptCount, result.Outcome, result.Score);
    }
}
