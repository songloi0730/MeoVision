using MeoVision.Core.Devices;

namespace MeoVision.Core.Storage;

/// <summary>
/// Ghi và đọc lại ảnh của lệnh (Chương 23, mục 23.4). Bản cài thật dùng thư viện ảnh của VisionPro
/// nên nằm ở MeoVision.Vision; App chỉ biết interface này — test được bằng bản giả.
/// </summary>
public interface IImageStore
{
    /// <summary>Phần mở rộng file, ví dụ ".bmp".</summary>
    string Extension { get; }

    void Save(CameraImage image, string file);

    CameraImage Load(string file);

    /// <summary>
    /// Ghi ảnh KÈM đồ hoạ kết quả (ảnh như trên màn hình) ra JPEG từ record của job (đối tượng của
    /// tầng Vision). Record không có ảnh nào thì không ghi và trả false.
    /// </summary>
    bool SaveAnnotated(object record, string preferredKey, string file);
}
