using System;
using System.Collections.Generic;
using System.IO;
using Cognex.VisionPro;
using Cognex.VisionPro.CalibFix;
using MeoVision.Core;
using MeoVision.Core.Calibration;
using MeoVision.Core.Localization;

namespace MeoVision.Vision.Commands;

/// <summary>
/// Tính hiệu chuẩn N điểm bằng <c>CogCalibNPointToNPoint</c> và ghi một
/// <c>CogCalibNPointToNPointTool</c> ra file (Chương 21, mục 21.5) — đúng loại file
/// <see cref="CogCalibrationLoader"/> đọc ở Chương 20.
/// </summary>
public sealed class CogNPointCalibrationBuilder : ICalibrationBuilder
{
    public CalibrationFit Fit(IReadOnlyList<CalibrationPoint> points, string outputFile)
    {
        Guard.ThrowIfNull(points, nameof(points));
        Guard.ThrowIfNull(outputFile, nameof(outputFile));
        CogCalibNPointToNPoint calibration = new();
        foreach (CalibrationPoint p in points)
        {
            calibration.AddPointPair(p.PixelX, p.PixelY, p.MachineX, p.MachineY);
        }
        try
        {
            calibration.Calibrate();
        }
        catch (Exception ex) when (ex.GetType().Namespace?.StartsWith("Cognex",
                                       StringComparison.Ordinal) == true)
        {
            // Ví dụ: các điểm thẳng hàng — không suy ra được phép biến đổi 2D.
            return CalibrationFit.Failed(Msg.Of("calibration.fitFailed", ex.Message));
        }

        // Sai số từng điểm trong KHÔNG GIAN ẢNH (pixel), cùng đơn vị với ComputedRMSError: đưa
        // toạ độ máy ngược về ảnh rồi so với vị trí dấu đã tìm.
        ICogTransform2D toPixel = calibration.GetComputedUncalibratedFromCalibratedTransform();
        List<double> residuals = new();
        foreach (CalibrationPoint p in points)
        {
            toPixel.MapPoint(p.MachineX, p.MachineY, out double ux, out double uy);
            residuals.Add(Math.Sqrt((ux - p.PixelX) * (ux - p.PixelX) +
                                    (uy - p.PixelY) * (uy - p.PixelY)));
        }

        string temp = outputFile + ".tmp";
        using (CogCalibNPointToNPointTool tool = new() { Calibration = calibration })
        {
            CogSerializer.SaveObjectToFile(tool, temp);
        }
        if (File.Exists(outputFile))
        {
            File.Replace(temp, outputFile, null);
        }
        else
        {
            File.Move(temp, outputFile);
        }
        return new CalibrationFit(null, calibration.ComputedRMSError, residuals);
    }
}
