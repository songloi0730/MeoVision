using System;
using System.Collections.Generic;

namespace MeoVision.Core.Products;

/// <summary>
/// Nội dung <c>product.json</c> — một SẢN PHẨM (Chương 19). Chỉ tham chiếu VAI TRÒ thiết bị và
/// MÃ KÊNH của máy, không chứa IP/cổng/serial: chép nguyên thư mục sản phẩm sang máy cùng loại là
/// dùng được, miễn máy đó có đủ vai trò và kênh.
/// </summary>
public sealed class ProductConfig
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; }

    /// <summary>Mã sản phẩm — phải trùng tên thư mục, ví dụ "P-1024".</summary>
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Bảng lệnh: máy gửi mã lệnh, phần mềm chạy dòng tương ứng.</summary>
    public List<CommandConfig> Commands { get; set; } = new();
}

/// <summary>
/// Lệnh dùng vision để làm gì (DESIGN_LOG [29]). Hai loại máy dùng vision rất khác nhau: máy KIỂM TRA
/// (test, AOI — sản phẩm phải qua trạm, kết quả là đạt/không đạt) và máy DẪN HƯỚNG (vision trả X, Y, góc
/// để điều khiển chuyển động). Bỏ qua kiểm chỉ có nghĩa với máy kiểm tra: không ai bịa được toạ độ.
/// </summary>
public static class CommandKind
{
    public const string Inspect = "Inspect";
    public const string Guide = "Guide";

    public static bool IsGuide(string? kind) =>
        string.Equals(kind, Guide, StringComparison.OrdinalIgnoreCase);

    public static bool IsValid(string? kind) =>
        string.Equals(kind, Inspect, StringComparison.OrdinalIgnoreCase) || IsGuide(kind);
}

/// <summary>Một dòng của bảng lệnh.</summary>
public sealed class CommandConfig
{
    /// <summary>Mã máy gửi tới, ví dụ "T1". Chữ hoa + số, tối đa 8 ký tự.</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Kênh nhận lệnh này (<c>machine.json → channels</c>).</summary>
    public string Channel { get; set; } = string.Empty;

    /// <summary>
    /// <see cref="CommandKind.Inspect"/> (mặc định — kiểm tra đạt/không đạt, được bỏ qua có kiểm soát)
    /// hoặc <see cref="CommandKind.Guide"/> (trả tham số điều khiển chuyển động — không bao giờ bỏ qua).
    /// </summary>
    public string Kind { get; set; } = CommandKind.Inspect;

    /// <summary>Vai trò camera chụp cho lệnh này.</summary>
    public string Camera { get; set; } = string.Empty;

    /// <summary>Tên file ToolBlock trong thư mục <c>jobs</c> — không đường dẫn.</summary>
    public string Job { get; set; } = string.Empty;

    /// <summary>Mã hiệu chuẩn của máy (thư mục trong <c>machine\calibration</c>).</summary>
    public string Calibration { get; set; } = string.Empty;

    /// <summary>Thời gian phơi sáng riêng của lệnh; null = giữ nguyên giá trị camera.</summary>
    public double? ExposureMs { get; set; }

    /// <summary>Brightness/Contrast của camera (0–1, camera nào hỗ trợ); null = giữ nguyên.</summary>
    public double? Brightness { get; set; }

    public double? Contrast { get; set; }

    /// <summary>Camera thêm — chụp trong cùng lệnh, ảnh vào terminal riêng.</summary>
    public List<CameraInput> ExtraCameras { get; set; } = new();

    public List<LightSetting> Lights { get; set; } = new();

    public int TimeoutMs { get; set; } = 1000;

    /// <summary>Số lần chụp lại khi lỗi chụp (không phải khi NG).</summary>
    public int Retry { get; set; }

    public DisplayRecords Display { get; set; } = new();

    /// <summary>Giá trị trả về máy, theo đúng thứ tự trong danh sách.</summary>
    public List<OutputConfig> Outputs { get; set; } = new();

    /// <summary>
    /// Tham số nhanh (Chương 22): terminal INPUT của job mà kỹ sư được chỉnh bằng thanh trượt, có
    /// khoảng cho phép. Giá trị nằm ở đây, không trong .vpp — sửa số không phải lưu lại job.
    /// </summary>
    public List<TunableConfig> Tunables { get; set; } = new();
}

/// <summary>Một tham số nhanh: terminal input kiểu số của job, khoảng cho phép, giá trị.</summary>
public sealed class TunableConfig
{
    /// <summary>Tên terminal input trong ToolBlock, ví dụ "MinArea".</summary>
    public string Terminal { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Unit { get; set; } = string.Empty;

    public double Min { get; set; }

    public double Max { get; set; }

    public double Step { get; set; } = 1;

    public double Value { get; set; }
}

/// <summary>Một kênh đèn bật khi chạy lệnh.</summary>
public sealed class LightSetting
{
    public string Role { get; set; } = string.Empty;

    /// <summary>Số kênh, đếm từ 1.</summary>
    public int Channel { get; set; }

    public int Intensity { get; set; }

    /// <summary>
    /// Chờ bao lâu sau khi bật đèn rồi mới chụp (ms) — đèn công suất lớn và bộ điều khiển chậm cần
    /// vài chục ms để sáng ổn định. Lệnh chờ theo kênh có thời gian lớn nhất.
    /// </summary>
    public int SettleMs { get; set; }
}

/// <summary>
/// Camera thêm của một lệnh: một lệnh chụp nhiều camera, mỗi ảnh vào một terminal input của job (như
/// một chức năng nhiều camera ở chương trình tham khảo). Camera chính vẫn là <c>camera</c> →
/// <c>InputImage</c>.
/// </summary>
public sealed class CameraInput
{
    public string Camera { get; set; } = string.Empty;

    /// <summary>Terminal input kiểu ảnh của job, khác <c>InputImage</c>.</summary>
    public string Terminal { get; set; } = string.Empty;

    /// <summary>Mã hiệu chuẩn cho ảnh này; rỗng = không gắn.</summary>
    public string Calibration { get; set; } = string.Empty;

    public double? ExposureMs { get; set; }

    public double? Brightness { get; set; }

    public double? Contrast { get; set; }
}

/// <summary>
/// Record của LastRun hiển thị trên màn chính (Chương 22): ảnh kết quả và ảnh gốc. Khoá record
/// chỉ có sau lần chạy đầu nên không kiểm được lúc nạp — rỗng = để giao diện tự chọn.
/// </summary>
public sealed class DisplayRecords
{
    public string Result { get; set; } = string.Empty;

    public string Raw { get; set; } = string.Empty;

    /// <summary>
    /// Khung hiển thị trên màn chính: 0 = lệnh có khung riêng; các lệnh cùng một số dùng CHUNG một
    /// khung (như DisplayID / InspectDisplay của hai chương trình tham khảo).
    /// </summary>
    public int Slot { get; set; }
}

/// <summary>
/// Một giá trị trả về: đọc từ terminal output của ToolBlock, cộng bù, kiểm giới hạn. Giới hạn và bù
/// nằm NGOÀI file .vpp để kỹ sư chỉnh số mà không mở job.
/// </summary>
public sealed class OutputConfig
{
    /// <summary>Tên terminal output trong ToolBlock.</summary>
    public string Terminal { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Unit { get; set; } = string.Empty;

    /// <summary>Định dạng số khi gửi cho máy, ví dụ "0.000".</summary>
    public string Format { get; set; } = "0.000";

    public double? Lower { get; set; }

    public double? Upper { get; set; }

    public double Offset { get; set; }
}
