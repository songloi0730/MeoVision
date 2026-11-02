using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using Cognex.VisionPro;
using Cognex.VisionPro.FGGigE;
using MeoVision.Core;
using MeoVision.Core.Contracts;
using MeoVision.Core.Exceptions;
using Microsoft.Extensions.Logging;

namespace MeoVision.Engine;

// Thu ảnh thật qua GigE Vision (Ch6) — mọi API xác nhận bằng reflection trên DLL cài thật trước
// khi viết (CogFrameGrabberGigEs, ICogFrameGrabber.CreateAcqFifo, ICogAcqTrigger.TriggerModel,
// ICogAcqStrobe*, CogStringCollection có indexer int). Compile-verify được trên máy này (có SDK
// cài) nhưng KHÔNG có camera GigE thật gắn vào — ConnectAsync sẽ throw thật (không phải lỗi code)
// nếu chạy trên máy không có camera, đúng hành vi cần có khi chưa gắn phần cứng.
public sealed class CognexCameraSource : ICameraSource
{
    private readonly ILogger<CognexCameraSource> _logger;
    private ICogAcqFifo? _acqFifo;

    public CognexCameraSource(ILogger<CognexCameraSource> logger)
    {
        Guard.ThrowIfNull(logger, nameof(logger));
        _logger = logger;
    }

    public bool IsConnected => _acqFifo is not null;

    public Task ConnectAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        CogFrameGrabberGigEs grabbers = new();
        if (grabbers.Count == 0)
        {
            throw new VisionEngineException(
                "Không tìm thấy frame grabber GigE nào — kiểm tra camera đã cắm/cấp nguồn và driver GigE Vision đã cài chưa.",
                VisionFailureReason.SystemError);
        }

        ICogFrameGrabber grabber = grabbers[0];
        string videoFormat = grabber.AvailableVideoFormats[0];
        _acqFifo = grabber.CreateAcqFifo(videoFormat, CogAcqFifoPixelFormatConstants.Format8Grey, 4, true);

        _logger.LogInformation("Đã kết nối camera GigE: {Name} ({SerialNumber}), format={Format}",
            grabber.Name, grabber.SerialNumber, videoFormat);
        return Task.CompletedTask;
    }

    public Task DisconnectAsync(CancellationToken ct = default)
    {
        _acqFifo = null;
        return Task.CompletedTask;
    }

    public Task ConfigureAsync(VisionRecipe recipe, CancellationToken ct = default)
    {
        Guard.ThrowIfNull(recipe, nameof(recipe));
        ICogAcqFifo acqFifo = _acqFifo
            ?? throw new VisionEngineException("Chưa ConnectAsync — gọi trước khi ConfigureAsync.");

        acqFifo.OwnedTriggerParams.TriggerModel = recipe.TriggerMode switch
        {
            CameraTriggerMode.FreeRun => CogAcqTriggerModelConstants.FreeRun,
            CameraTriggerMode.Auto => CogAcqTriggerModelConstants.Auto,
            CameraTriggerMode.Semi => CogAcqTriggerModelConstants.Semi,
            CameraTriggerMode.Manual => CogAcqTriggerModelConstants.Manual,
            _ => CogAcqTriggerModelConstants.Semi
        };

        acqFifo.OwnedStrobeParams.StrobeEnabled = recipe.StrobeEnabled;
        if (recipe.StrobeEnabled)
        {
            acqFifo.OwnedStrobeDelayParams.StrobeDelay = recipe.StrobeDelayUs;
            acqFifo.OwnedStrobePulseDurationParams.StrobePulseDuration = recipe.StrobePulseDurationUs;
        }

        // Đẩy cấu hình xuống camera thật — bắt buộc sau khi đổi property (Ch6: "Operator.Prepare()
        // để đẩy xuống camera"), AutoPrepareEnabled mặc định True nên gọi thêm ở đây là tường minh,
        // không dựa vào hành vi ngầm định.
        acqFifo.Prepare();
        _logger.LogInformation(
            "Đã cấu hình camera: TriggerMode={TriggerMode} StrobeEnabled={StrobeEnabled}",
            recipe.TriggerMode, recipe.StrobeEnabled);
        return Task.CompletedTask;
    }

    public async Task<Bitmap> GrabAsync(CancellationToken ct = default)
    {
        ICogAcqFifo acqFifo = _acqFifo
            ?? throw new VisionEngineException("Chưa ConnectAsync — gọi trước khi GrabAsync.");

        return await Task.Run(() =>
        {
            // Acquire() = chụp 1 lần, khoá tới khi có trigger hoặc hết ICogAcqFifo.Timeout (mặc
            // định 10s) — đơn giản hơn cặp StartAcquire()/CompleteAcquire() sách dạy (hữu ích khi
            // cần làm việc khác giữa lúc "arm" và lúc "hoàn tất"; không cần ở đây vì đã trong Task.Run).
            ICogImage image = acqFifo.Acquire(out int _);
            try
            {
                return ((CogImage8Grey)image).ToBitmap();
            }
            finally
            {
                (image as IDisposable)?.Dispose();
            }
        }, ct).ConfigureAwait(false);
    }

    public void Dispose()
    {
        _acqFifo = null;
    }
}
