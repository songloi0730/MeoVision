using System;
using System.Collections.Generic;
using MeoVision.Core.Localization;
using MeoVision.Core.Products;

namespace MeoVision.Core.Calibration;

/// <summary>
/// <c>machine\calibration\&lt;mã&gt;\calibration.json</c> — cách chụp và tìm dấu khi máy dẫn hiệu
/// chuẩn (Chương 21, mục 21.5). Kết quả ghi ra <c>calibration.vpp</c> cùng thư mục — chính file
/// mà bộ máy chạy lệnh nạp (Chương 20).
/// </summary>
public sealed class CalibrationConfig
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; }

    /// <summary>Vai trò camera nhìn dấu.</summary>
    public string Camera { get; set; } = string.Empty;

    public double? ExposureMs { get; set; }

    public List<LightSetting> Lights { get; set; } = new();

    /// <summary>
    /// ToolBlock tìm dấu: input <c>InputImage</c>, output <c>X</c>, <c>Y</c> (pixel).
    /// </summary>
    public string MarkJob { get; set; } = "mark.vpp";

    /// <summary>Số điểm tối thiểu để tính (N điểm cần ≥ 3 cho phép biến đổi tuyến tính).</summary>
    public int MinPoints { get; set; } = 9;

    /// <summary>
    /// RMS lớn nhất chấp nhận, tính trong không gian ẢNH VÀO (pixel) — Chương 7, mục 7.7.2.
    /// </summary>
    public double MaxRmsPixels { get; set; } = 1.0;
}

/// <summary>Một cặp điểm: vị trí dấu trên ảnh và toạ độ máy lúc chụp.</summary>
public sealed record CalibrationPoint(int Index, double PixelX, double PixelY, double MachineX,
    double MachineY);

/// <summary>Kết quả tính: RMS và sai số từng điểm (pixel) để thấy điểm nào kéo RMS lên.</summary>
public sealed record CalibrationFit(Msg? Error, double Rms, IReadOnlyList<double> Residuals)
{
    public bool Ok => Error is null;

    public static CalibrationFit Failed(Msg error) => new(error, double.NaN, Array.Empty<double>());
}

/// <summary>
/// Tính hiệu chuẩn N điểm từ các cặp điểm và ghi file (bản cài thật dùng VisionPro, ở
/// MeoVision.Vision). Ghi kiểu file tạm + đổi tên: hỏng giữa chừng không để lại file dở.
/// </summary>
public interface ICalibrationBuilder
{
    CalibrationFit Fit(IReadOnlyList<CalibrationPoint> points, string outputFile);
}
