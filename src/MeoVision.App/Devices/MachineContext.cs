using MeoVision.Core.Devices;

namespace MeoVision.App.Devices;

/// <summary>
/// Cấu hình máy đã nạp và kiểm ở bước "Cấu hình máy" — các bước sau (sản phẩm Chương 19, kênh
/// Chương 21) dùng lại, không đọc file lần nữa.
/// </summary>
public sealed class MachineContext
{
    public MachineConfig? Config { get; set; }
}
