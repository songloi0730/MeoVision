using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Cognex.VisionPro;
using Cognex.VisionPro.ToolBlock;
using MeoVision.Core;
using MeoVision.Core.Contracts;
using MeoVision.Core.Exceptions;
using Microsoft.Extensions.Logging;

namespace MeoVision.Engine;

// DUY NHẤT project được using Cognex.VisionPro.* xử lý ảnh thật (skill meovision-engine-patterns).
// Mọi API Cog* dùng ở đây đã kiểm chứng bằng reflection trực tiếp trên DLL cài thật tại
// C:\Program Files\Cognex\VisionPro\ReferencedAssemblies\ (không suy đoán từ tài liệu/mẫu code
// tham khảo) — CogToolBlockTerminal.Value thật sự nằm ở Cognex.VisionPro.dll (không phải
// ToolGroup.dll dù CogToolBlock chính nó lại nằm ở ToolGroup.dll — 2 assembly khác nhau cho cùng
// 1 namespace Cognex.VisionPro.ToolBlock, dễ đoán sai nếu không kiểm chứng).
public sealed class CognexVisionEngine : IVisionEngine
{
    private readonly ILogger<CognexVisionEngine> _logger;
    private readonly ICameraSource? _cameraSource;
    private CogToolBlock? _toolBlock;
    private VisionRecipe? _recipe;

    // cameraSource=null vẫn dùng được — bắt buộc VisionJobRequest.SourceImage cấp sẵn (test/replay
    // ảnh, hoặc chưa gắn camera thật). Không Dispose _cameraSource trong Dispose() của lớp này —
    // DI container/host sở hữu vòng đời camera, không phải Engine (Engine chỉ mượn để cấu hình/chụp).
    public CognexVisionEngine(ILogger<CognexVisionEngine> logger, ICameraSource? cameraSource = null)
    {
        Guard.ThrowIfNull(logger, nameof(logger));
        _logger = logger;
        _cameraSource = cameraSource;
    }

    public string EngineName => "Cognex.VisionPro";

    public bool IsJobLoaded => _toolBlock is not null;

    public event EventHandler<VisionResult>? InspectionCompleted;

    public async Task LoadRecipeAsync(VisionRecipe recipe, CancellationToken ct = default)
    {
        Guard.ThrowIfNull(recipe, nameof(recipe));

        using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(recipe.JobLoadTimeoutMs);

        try
        {
            await Task.Run(() => LoadToolBlock(recipe), cts.Token).ConfigureAwait(false);
            if (_cameraSource is not null)
            {
                await _cameraSource.ConfigureAsync(recipe, cts.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new VisionEngineTimeoutException(
                $"Nạp job vượt quá {recipe.JobLoadTimeoutMs}ms: {recipe.JobFilePath}", recipe.JobName);
        }
    }

    private void LoadToolBlock(VisionRecipe recipe)
    {
        if (!File.Exists(recipe.JobFilePath))
        {
            throw new VisionJobLoadException($"Không tìm thấy file job: {recipe.JobFilePath}", recipe.JobName);
        }

        object loaded;
        try
        {
            // CogSerializer.LoadObjectFromFile(string, Type) : object — xác nhận chữ ký thật qua
            // reflection, không dùng overload có CogSerializationOptionsConstants (không cần cho
            // trường hợp đọc file .vpp thông thường).
            loaded = CogSerializer.LoadObjectFromFile(recipe.JobFilePath, typeof(CogToolBlock));
        }
#pragma warning disable CA1031
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            throw new VisionJobLoadException($"CogSerializer lỗi khi nạp {recipe.JobFilePath}", ex, recipe.JobName);
        }

        _toolBlock?.Dispose();
        _toolBlock = loaded as CogToolBlock
            ?? throw new VisionJobLoadException($"{recipe.JobFilePath} không phải CogToolBlock", recipe.JobName);

        _recipe = recipe;
        _logger.LogInformation("Đã nạp job {JobFilePath} (job={JobName}) vào Engine thật",
            recipe.JobFilePath, recipe.JobName);
    }

    public Task TeachPatternAsync(VisionTrainRegion trainRegion, CancellationToken ct = default)
    {
        Guard.ThrowIfNull(trainRegion, nameof(trainRegion));
        ct.ThrowIfCancellationRequested();

        // Teach thật (vẽ vùng train cho PMAlign/PatMax, lưu lại .vpp) cần 1 màn hình riêng (Ch14
        // §14.4.2) — không nằm trong phạm vi V0-V3, throw rõ ràng thay vì giả vờ hỗ trợ.
        throw new NotSupportedException(
            "TeachPatternAsync chưa hỗ trợ ở Engine thật — cần màn hình Teach riêng (V4+, xem CLAUDE.md).");
    }

    public async Task<VisionResult> InspectAsync(VisionJobRequest request, CancellationToken ct = default)
    {
        Guard.ThrowIfNull(request, nameof(request));

        CogToolBlock toolBlock = _toolBlock
            ?? throw new VisionEngineException("Chưa nạp job — gọi LoadRecipeAsync trước.");
        VisionRecipe recipe = _recipe ?? throw new VisionEngineException("Recipe hiện hành bị thiếu.");

        using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(recipe.CycleTimeoutMs);

        try
        {
            Bitmap sourceImage = request.SourceImage
                ?? await AcquireFromCameraAsync(recipe, cts.Token).ConfigureAwait(false);
            return await Task.Run(() => RunOnce(request, sourceImage, toolBlock, recipe), cts.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new VisionEngineTimeoutException($"Chu trình vượt quá {recipe.CycleTimeoutMs}ms", recipe.JobName);
        }
    }

    private async Task<Bitmap> AcquireFromCameraAsync(VisionRecipe recipe, CancellationToken ct)
    {
        if (_cameraSource is null)
        {
            // Không có ICameraSource (chưa gắn camera thật/DI chưa đăng ký) VÀ request không cấp
            // sẵn ảnh — không còn nguồn ảnh nào khác, throw rõ ràng thay vì NullReferenceException
            // mơ hồ ở bước sau.
            throw new VisionEngineException(
                "Không có ICameraSource và VisionJobRequest.SourceImage rỗng — không có ảnh để xử lý.",
                VisionFailureReason.SystemError, recipe.JobName);
        }

        return await _cameraSource.GrabAsync(ct).ConfigureAwait(false);
    }

    private VisionResult RunOnce(
        VisionJobRequest request, Bitmap sourceImage, CogToolBlock toolBlock, VisionRecipe recipe)
    {
        // Khai báo CogImage8Grey cụ thể (không phải ICogImage) — interface ICogImage không tự kế
        // thừa IDisposable dù bản concrete này có, xác nhận qua lỗi biên dịch CS1674 thật.
        using CogImage8Grey cogImage = new(sourceImage);
        toolBlock.Inputs[TerminalNames.InputImage].Value = cogImage;
        toolBlock.Run();

        ICogRunStatus status = toolBlock.RunStatus;
        if (status.Exception is not null)
        {
            throw new VisionEngineException(
                $"CogToolBlock.Run() ném exception: {status.Exception.Message}", status.Exception,
                VisionFailureReason.SystemError, recipe.JobName);
        }

        return MapToVisionResult(request, sourceImage, toolBlock, status, recipe);
    }

    private VisionResult MapToVisionResult(
        VisionJobRequest request, Bitmap sourceImage, CogToolBlock toolBlock, ICogRunStatus status,
        VisionRecipe recipe)
    {
        VisionOutcome outcome = status.Result switch
        {
            CogToolResultConstants.Accept => VisionOutcome.Accept,
            CogToolResultConstants.Warning => VisionOutcome.Warning,
            CogToolResultConstants.Reject => VisionOutcome.Reject,
            _ => VisionOutcome.Error
        };

        double score = ReadOutputDouble(toolBlock, TerminalNames.Score);
        double x = ReadOutputDouble(toolBlock, TerminalNames.PartX);
        double y = ReadOutputDouble(toolBlock, TerminalNames.PartY);
        double angle = ReadOutputDouble(toolBlock, TerminalNames.PartAngle);
        string? code = ReadOutputString(toolBlock, TerminalNames.Code);

        VisionFailureReason reason = outcome switch
        {
            VisionOutcome.Accept or VisionOutcome.Warning => VisionFailureReason.None,
            VisionOutcome.Reject when score < recipe.MinAcceptScore => VisionFailureReason.PartNotFound,
            VisionOutcome.Reject => VisionFailureReason.MeasurementFailed,
            _ => VisionFailureReason.SystemError
        };

        List<VisionCheckResult> checks = new()
        {
            new VisionCheckResult("Score", score >= recipe.MinAcceptScore, score, recipe.MinAcceptScore, null)
        };

        VisionResult result = new(
            CorrelationId: request.CorrelationId,
            JobName: recipe.JobName,
            Outcome: outcome,
            FailureReason: reason,
            Score: score,
            X: x,
            Y: y,
            AngleDeg: angle,
            Code: code,
            Checks: checks,
            TimestampUtc: DateTimeOffset.UtcNow,
            ProcessingTimeMs: status.ProcessingTime,
            Image: (Bitmap)sourceImage.Clone());

        _logger.LogInformation(
            "Kiểm tra thật {CorrelationId} outcome={Outcome} score={Score:F3} msg={Message}",
            result.CorrelationId, result.Outcome, result.Score, status.Message);

        InspectionCompleted?.Invoke(this, result);
        return result;
    }

    private static double ReadOutputDouble(CogToolBlock toolBlock, string terminalName)
    {
        if (!toolBlock.Outputs.Contains(terminalName))
        {
            return 0.0;
        }

        return toolBlock.Outputs[terminalName].Value switch
        {
            double d => d,
            float f => f,
            int i => i,
            _ => 0.0
        };
    }

    private static string? ReadOutputString(CogToolBlock toolBlock, string terminalName) =>
        toolBlock.Outputs.Contains(terminalName) ? toolBlock.Outputs[terminalName].Value as string : null;

    public void Dispose()
    {
        _toolBlock?.Dispose();
    }
}
