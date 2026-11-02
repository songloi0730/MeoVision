using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cognex.VisionPro;
using Cognex.VisionPro.Exceptions;
using MeoVision.Core;
using MeoVision.Core.Devices;
using MeoVision.Core.Localization;

namespace MeoVision.Vision.Devices;

/// <summary>
/// Camera GigE Vision qua bộ thu ảnh của VisionPro, chọn theo SỐ SERIAL (Chương 18, mục 18.3) —
/// không theo thứ tự trong danh sách, vì thứ tự đổi khi thêm/bớt camera hay đổi card mạng. Các API
/// thu ảnh đã giới thiệu ở Chương 6 (mục 6.1–6.2) và Chương 13 (mục 13.4).
/// </summary>
public sealed class CognexGigECamera : ICamera
{
    private readonly string _serialNumber;
    private readonly string _videoFormat;
    private readonly double _timeoutMs;
    private CogFrameGrabbers? _grabbers;
    private ICogAcqFifo? _fifo;
    private double? _lastExposureMs;
    private double? _lastBrightness;
    private double? _lastContrast;

    public CognexGigECamera(string role, string serialNumber, string videoFormat,
        double timeoutMs)
    {
        Guard.ThrowIfNull(role, nameof(role));
        Guard.ThrowIfNull(serialNumber, nameof(serialNumber));
        Guard.ThrowIfNull(videoFormat, nameof(videoFormat));
        Role = role;
        _serialNumber = serialNumber;
        _videoFormat = videoFormat;
        _timeoutMs = timeoutMs;
    }

    public string Role { get; }

    public string Description => "GigE SN " + _serialNumber;

    public Task OpenAsync(CancellationToken ct)
    {
        // Mở/đóng đi qua tầng COM thu ảnh: tuần tự hoá với mọi camera khác và với kiểm license
        // (VisionProLock). Watchdog có thể mở lại camera này trong lúc camera khác đang mở.
        lock (VisionProLock.Gate)
        {
            OpenLocked();
        }
        return Task.CompletedTask;
    }

    private void OpenLocked()
    {
        CloseLocked();
        // Giữ danh sách bộ thu ảnh suốt thời gian camera mở: FIFO tạo từ nó còn dùng tới.
        _grabbers = new CogFrameGrabbers();
        List<ICogFrameGrabber> all = _grabbers.Cast<ICogFrameGrabber>().ToList();
        ICogFrameGrabber? grabber = all.FirstOrDefault(g =>
            string.Equals(g.SerialNumber, _serialNumber, StringComparison.OrdinalIgnoreCase));
        if (grabber is null)
        {
            // Liệt kê serial TÌM THẤY: lỗi gõ serial nhìn ra ngay, không phải đoán.
            string found = all.Count == 0
                ? "-"
                : string.Join(", ", all.Select(g => g.SerialNumber + " (" + g.Name + ")"));
            throw new DeviceException(Msg.Of("camera.gige.notFound", Role, _serialNumber,
                found));
        }

        List<string> formats = grabber.AvailableVideoFormats.Cast<string>().ToList();
        string format = _videoFormat.Length == 0 ? formats.FirstOrDefault() ?? string.Empty
                                                 : _videoFormat;
        if (!formats.Contains(format))
        {
            throw new DeviceException(Msg.Of("camera.gige.badFormat", Role, format,
                string.Join(", ", formats)));
        }

        _fifo = grabber.CreateAcqFifo(format, CogAcqFifoPixelFormatConstants.Format8Grey, 0,
            true);
        _fifo.Timeout = _timeoutMs;           // ms (mặc định của VisionPro là 10 000 ms)
        _fifo.TimeoutEnabled = true;
        ICogAcqTrigger? trigger = _fifo.OwnedTriggerParams;
        if (trigger is not null)
        {
            // Chương 18: chụp theo lệnh phần mềm. Trigger cứng từ PLC — Chương 20–21.
            trigger.TriggerModel = CogAcqTriggerModelConstants.Manual;
            trigger.TriggerEnabled = true;
        }
        _fifo.Prepare();
        _lastExposureMs = null;
        _lastBrightness = null;
        _lastContrast = null;
    }

    public Task<CameraImage> GrabAsync(CaptureSettings settings, CancellationToken ct)
    {
        Guard.ThrowIfNull(settings, nameof(settings));
        ICogAcqFifo fifo = _fifo ?? throw new DeviceException(Msg.Of("device.notConnected",
            Role));

        // Chỉ ghi exposure xuống camera khi ĐỔI — ghi + Prepare() mỗi lần chụp tốn vài chục ms.
        if (settings.ExposureMs is double exposure && exposure != _lastExposureMs)
        {
            ICogAcqExposure? exposureParams = fifo.OwnedExposureParams;
            if (exposureParams is null)
            {
                throw new DeviceException(Msg.Of("camera.exposureNotSupported", Role));
            }
            exposureParams.Exposure = exposure;
            fifo.Prepare();
            _lastExposureMs = exposure;
        }
        // Brightness/Contrast (0–1): camera không hỗ trợ thì báo lỗi rõ thay vì chụp với giá trị cũ.
        if (settings.Brightness is double brightness && brightness != _lastBrightness)
        {
            ICogAcqBrightness b = fifo.OwnedBrightnessParams ??
                throw new DeviceException(Msg.Of("camera.paramNotSupported", Role, "Brightness"));
            b.Brightness = brightness;
            fifo.Prepare();
            _lastBrightness = brightness;
        }
        if (settings.Contrast is double contrast && contrast != _lastContrast)
        {
            ICogAcqContrast c = fifo.OwnedContrastParams ??
                throw new DeviceException(Msg.Of("camera.paramNotSupported", Role, "Contrast"));
            c.Contrast = contrast;
            fifo.Prepare();
            _lastContrast = contrast;
        }

        ICogImage image;
        try
        {
            image = fifo.Acquire(out _);
        }
        catch (Exception ex) when (ex is CogAcqTimeoutException or CogAcqOverrunException or
                                   CogAcqMissedException or CogAcqTimingException)
        {
            // Một khung hụt (mất gói, trigger lỡ) — camera vẫn kết nối, chụp lại được (Chương 20).
            // Lỗi khác (rút cáp, phần cứng) để DeviceManager đánh dấu hỏng và nối lại.
            throw new DeviceException(Msg.Of("camera.grabFailed", Role, ex.Message), ex,
                transient: true);
        }
        return Task.FromResult<CameraImage>(new CogCameraImage(image, Description,
            DateTime.Now));
    }

    public void Close()
    {
        lock (VisionProLock.Gate)
        {
            CloseLocked();
        }
    }

    private void CloseLocked()
    {
        // Không Disconnect ở đây: ngắt hẳn bộ thu ảnh là bước cuối của tắt máy
        // (IVisionRuntime.ReleaseHardware, Chương 17, mục 17.7.2); Close chỉ trả FIFO.
        (_fifo as IDisposable)?.Dispose();
        _fifo = null;
        // CogFrameGrabbers cài IDisposable TƯỜNG MINH: gọi .Dispose() thẳng không biên dịch.
        (_grabbers as IDisposable)?.Dispose();
        _grabbers = null;
    }

    public void Dispose() => Close();
}
