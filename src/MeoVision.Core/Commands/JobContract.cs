namespace MeoVision.Core.Commands;

/// <summary>
/// Hợp đồng giữa bộ máy chạy lệnh và mọi file job (Chương 19–20): tên terminal input nhận ảnh.
/// Output do từng lệnh khai trong <c>product.json</c>.
/// </summary>
public static class JobContract
{
    /// <summary>Ảnh vừa chụp — đã gắn hiệu chuẩn nếu lệnh có hiệu chuẩn.</summary>
    public const string InputImage = "InputImage";
}
