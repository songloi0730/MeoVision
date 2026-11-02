using System;
using System.IO;
using Cognex.VisionPro;
using Cognex.VisionPro.CalibFix;
using MeoVision.Core;
using MeoVision.Core.Commands;
using MeoVision.Core.Devices;
using MeoVision.Core.Localization;
using MeoVision.Vision.Devices;

namespace MeoVision.Vision.Commands;

/// <summary>
/// Mở hiệu chuẩn <c>machine\calibration\&lt;mã&gt;\calibration.vpp</c> (Chương 20, mục 20.3): một
/// tool hiệu chuẩn N điểm hoặc bàn cờ ĐÃ hiệu chuẩn. Chỉ lấy phép biến đổi một lần lúc nạp; mỗi
/// lệnh gắn nó vào cây toạ độ của ảnh — không chạy lại tool hiệu chuẩn.
/// </summary>
public sealed class CogCalibrationLoader : ICalibrationLoader
{
    public const string FileName = "calibration.vpp";

    private readonly string _root;

    public CogCalibrationLoader(string root)
    {
        Guard.ThrowIfNull(root, nameof(root));
        _root = root;
    }

    public CalibrationLoad Load(string id)
    {
        Guard.ThrowIfNull(id, nameof(id));
        string file = Path.Combine(_root, id, FileName);
        if (!File.Exists(file))
        {
            return new CalibrationLoad(null, Msg.Of("calibration.missing", id, file));
        }

        object loaded;
        try
        {
            lock (VisionProLock.Gate)
            {
                loaded = CogSerializer.LoadObjectFromFile(file);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException ||
                                   ex.GetType().Namespace?.StartsWith("Cognex",
                                       StringComparison.Ordinal) == true ||
                                   ex is System.Runtime.Serialization.SerializationException)
        {
            return new CalibrationLoad(null, Msg.Of("calibration.loadFailed", id, ex.Message));
        }

        // Hai kiểu tool hiệu chuẩn hay gặp; tool chưa bấm Calibrate thì không có phép biến đổi.
        (bool known, bool calibrated, ICogTransform2D? transform) = loaded switch
        {
            CogCalibNPointToNPointTool n => (true, n.Calibration.Calibrated,
                n.Calibration.Calibrated
                    ? n.Calibration.GetComputedUncalibratedFromCalibratedTransform()
                    : null),
            CogCalibCheckerboardTool c => (true, c.Calibration.Calibrated,
                c.Calibration.Calibrated
                    ? c.Calibration.GetComputedUncalibratedFromCalibratedTransform()
                    : null),
            _ => (false, false, null),
        };
        if (transform is null)
        {
            string type = loaded?.GetType().Name ?? "null";
            (loaded as IDisposable)?.Dispose();
            return new CalibrationLoad(null, known && !calibrated
                ? Msg.Of("calibration.notCalibrated", id, type)
                : Msg.Of("calibration.wrongType", id, type));
        }
        return new CalibrationLoad(new CogCalibration(id, transform, loaded as IDisposable), null);
    }
}

/// <summary>Hiệu chuẩn đã nạp: một phép biến đổi "mm → pixel" gắn vào mọi ảnh của lệnh.</summary>
public sealed class CogCalibration : ICalibration
{
    /// <summary>Tên hệ toạ độ gắn vào ảnh; job đọc kết quả ở "@\Calibrated".</summary>
    public const string SpaceName = "Calibrated";

    private readonly ICogTransform2D _transform;
    private readonly IDisposable? _owner;
    private readonly object _sync = new();

    /// <param name="owner">Tool hiệu chuẩn sinh ra phép biến đổi — giữ sống tới khi Dispose.</param>
    public CogCalibration(string id, ICogTransform2D transform, IDisposable? owner)
    {
        Guard.ThrowIfNull(id, nameof(id));
        Guard.ThrowIfNull(transform, nameof(transform));
        Id = id;
        _transform = transform;
        _owner = owner;
    }

    public string Id { get; }

    public void Apply(CameraImage image)
    {
        ICogImage cog = CogCameraImage.From(image);
        // copyTransform = true: mỗi ảnh một bản sao, ảnh này bị Dispose không ảnh hưởng ảnh khác.
        // Khoá vì hai camera dùng chung một hiệu chuẩn có thể gọi cùng lúc.
        lock (_sync)
        {
            cog.CoordinateSpaceTree.AddSpace("@", SpaceName, _transform, true,
                CogAddSpaceConstants.ReplaceDuplicate);
        }
        cog.SelectedSpaceName = "@\\" + SpaceName;
    }

    public void Dispose() => _owner?.Dispose();
}
