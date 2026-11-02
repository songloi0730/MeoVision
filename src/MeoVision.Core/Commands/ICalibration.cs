using System;
using MeoVision.Core.Devices;
using MeoVision.Core.Localization;

namespace MeoVision.Core.Commands;

/// <summary>
/// Hiệu chuẩn của một camera, đã nạp (Chương 20, mục 20.3). Gắn vào ảnh vừa chụp một hệ toạ độ
/// "Calibrated" và chọn nó — tool trong job đọc kết quả bằng mm thay vì pixel.
/// </summary>
public interface ICalibration : IDisposable
{
    /// <summary>Mã hiệu chuẩn, trùng tên thư mục trong <c>machine\calibration</c>.</summary>
    string Id { get; }

    void Apply(CameraImage image);
}

/// <summary>Mở hiệu chuẩn theo mã. Bản cài thật đọc file VisionPro nên nằm ở MeoVision.Vision.</summary>
public interface ICalibrationLoader
{
    CalibrationLoad Load(string id);
}

/// <summary>Kết quả mở hiệu chuẩn: hoặc dùng được, hoặc một lỗi nói rõ vì sao.</summary>
public sealed record CalibrationLoad(ICalibration? Calibration, Msg? Error);
