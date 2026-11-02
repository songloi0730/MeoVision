using System;
using System.Collections.Generic;
using System.Linq;
using Cognex.VisionPro;
using MeoVision.Core;
using MeoVision.Core.Contracts;
using Microsoft.Extensions.Logging;

namespace MeoVision.Engine;

/// <summary>
/// Môi trường VisionPro thật (Chương 17, mục 17.4 và 17.7). API đã đối chiếu trên 9.0 CR2:
/// <c>CogMisc.VisionProReleaseNumber()</c> trả "9.0 CR2"; <c>CogMisc.GetLicensedFeatures</c>
/// trả tên có tiền tố "Vx" (VxPatMax, VxBlob, VxCalib...).
/// </summary>
public sealed class CognexVisionRuntime : IVisionRuntime
{
    private readonly ILogger<CognexVisionRuntime> _logger;

    public CognexVisionRuntime(ILogger<CognexVisionRuntime> logger)
    {
        Guard.ThrowIfNull(logger, nameof(logger));
        _logger = logger;
    }

    public VisionRuntimeInfo Probe(IReadOnlyCollection<string> requiredFeatures)
    {
        Guard.ThrowIfNull(requiredFeatures, nameof(requiredFeatures));

        string version = CogMisc.VisionProReleaseNumber();
        // So với DANH SÁCH đầy đủ thay vì gọi IsLicensedFeatureEnabled từng tên: trên 9.0 CR2,
        // IsLicensedFeatureEnabled("PatMax") trả false dù máy có VxPatMax, còn "Blob" lại trả
        // true — tên không chuẩn cho kết quả khó đoán. Danh sách thì so được chính xác.
        List<string> licensed = new();
        using (CogStringCollection features = CogMisc.GetLicensedFeatures(false))
        {
            foreach (string feature in features)
            {
                licensed.Add(feature);
            }
        }

        List<string> missing = requiredFeatures
            .Where(f => !licensed.Contains(f, StringComparer.OrdinalIgnoreCase))
            .ToList();

        _logger.LogInformation("VisionPro {Version}, {Count} tinh nang co license", version,
            licensed.Count);
        return new VisionRuntimeInfo("Cognex VisionPro", version, licensed, missing, false);
    }

    public void ReleaseHardware()
    {
        // Tài liệu Cognex (ICogFrameGrabber.Disconnect): gọi trước khi thoát chương trình, với
        // AllowRecovery = false chỉ khi ngay trước lúc kết thúc — các trường hợp khác dùng true.
        using CogFrameGrabbers grabbers = new();
        foreach (ICogFrameGrabber grabber in grabbers)
        {
            try
            {
                grabber.Disconnect(false);
                _logger.LogInformation("Da ngat frame grabber {Name}", grabber.Name);
            }
            catch (Exception ex)
            {
                // Không để một grabber lỗi chặn việc ngắt các grabber còn lại.
                _logger.LogError(ex, "Ngat frame grabber {Name} that bai", grabber.Name);
            }
        }
    }
}
