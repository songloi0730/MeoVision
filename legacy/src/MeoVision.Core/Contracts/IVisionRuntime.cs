using System.Collections.Generic;

namespace MeoVision.Core.Contracts;

/// <summary>
/// Môi trường chạy của thư viện vision: phiên bản, tính năng có license, và việc trả phần cứng
/// khi thoát. Tách khỏi <see cref="IVisionEngine"/> vì đây là việc của VÒNG ĐỜI ỨNG DỤNG
/// (khởi động/tắt máy — Chương 17), không phải của một lần kiểm tra.
/// </summary>
public interface IVisionRuntime
{
    /// <summary>Đọc phiên bản + danh sách tính năng có license, đối chiếu với danh sách cần có.</summary>
    VisionRuntimeInfo Probe(IReadOnlyCollection<string> requiredFeatures);

    /// <summary>
    /// Ngắt mọi kết nối phần cứng của thư viện (frame grabber/camera). Gọi MỘT lần, ở bước cuối
    /// của quá trình tắt máy — sau khi mọi chu kỳ đã dừng.
    /// </summary>
    void ReleaseHardware();
}

/// <summary>Kết quả kiểm môi trường vision lúc khởi động.</summary>
public sealed record VisionRuntimeInfo(
    string LibraryName,
    string Version,
    IReadOnlyList<string> LicensedFeatures,
    IReadOnlyList<string> MissingFeatures,
    bool IsSimulated)
{
    public bool AllRequiredFeaturesLicensed => MissingFeatures.Count == 0;
}
