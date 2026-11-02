using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using MeoVision.Core;
using MeoVision.Core.Contracts;
using Microsoft.Extensions.Logging;

namespace MeoVision.Engine.Simulated;

// Cố tình ĐƠN GIẢN — không vẽ lại phần/pad/lỗi như SimulatedVisionEngine (đó vẫn là nơi mô phỏng
// kết quả kiểm tra duy nhất). Camera giả lập ở đây chỉ trả 1 khung xám trơn, vì SimulatedVisionEngine
// không thật sự cần ảnh của lớp này để mô phỏng (tự vẽ ảnh riêng qua SyntheticPartRenderer) — lớp
// này tồn tại để bản giả lập của MeoVision.UI/WorkStation.Demo có ICameraSource cùng shape với bản
// thật khi muốn thử nghiệm luồng "camera riêng, engine riêng" mà không cần camera vật lý.
public sealed class SimulatedCameraSource : ICameraSource
{
    private readonly ILogger<SimulatedCameraSource> _logger;
    private bool _isConnected;

    public SimulatedCameraSource(ILogger<SimulatedCameraSource> logger)
    {
        Guard.ThrowIfNull(logger, nameof(logger));
        _logger = logger;
    }

    public bool IsConnected => _isConnected;

    public Task ConnectAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _isConnected = true;
        _logger.LogInformation("Camera giả lập: đã kết nối (không có phần cứng thật phía sau)");
        return Task.CompletedTask;
    }

    public Task DisconnectAsync(CancellationToken ct = default)
    {
        _isConnected = false;
        return Task.CompletedTask;
    }

    public Task ConfigureAsync(VisionRecipe recipe, CancellationToken ct = default)
    {
        Guard.ThrowIfNull(recipe, nameof(recipe));
        _logger.LogInformation(
            "Camera giả lập: ghi nhận TriggerMode={TriggerMode} StrobeEnabled={StrobeEnabled} (không áp dụng thật)",
            recipe.TriggerMode, recipe.StrobeEnabled);
        return Task.CompletedTask;
    }

    public Task<Bitmap> GrabAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        Bitmap frame = new(640, 480);
        using Graphics g = Graphics.FromImage(frame);
        g.Clear(Color.FromArgb(90, 90, 90));
        return Task.FromResult(frame);
    }

    public void Dispose()
    {
        _isConnected = false;
    }
}
