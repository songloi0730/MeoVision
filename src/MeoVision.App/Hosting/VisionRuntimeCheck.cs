using System.Collections.Generic;
using MeoVision.Core;
using MeoVision.Core.Localization;
using MeoVision.Core.Vision;

namespace MeoVision.App.Hosting;

/// <summary>
/// Bước khởi động "môi trường thư viện vision" (Chương 17, mục 17.4): đọc phiên bản, so
/// danh sách tính năng có license với danh sách job cần. Thiếu license thì phát hiện NGAY
/// LÚC MỞ MÁY, không phải lúc job đầu tiên ném exception giữa ca.
/// </summary>
public static class VisionRuntimeCheck
{
    public static StepOutcome Run(IVisionRuntime runtime, IReadOnlyCollection<string> required)
    {
        Guard.ThrowIfNull(runtime, nameof(runtime));
        Guard.ThrowIfNull(required, nameof(required));

        VisionRuntimeInfo info = runtime.Probe(required);
        return info.AllRequiredFeaturesLicensed
            ? StepOutcome.Ok(Msg.Of("startup.vision.ok", info.LibraryName, info.Version,
                info.LicensedFeatures.Count))
            : StepOutcome.Failed(Msg.Of("startup.vision.missing", info.LibraryName, info.Version,
                string.Join(", ", info.MissingFeatures)));
    }
}
