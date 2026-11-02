using MeoVision.App.Results;

namespace MeoVision.App;

/// <summary>
/// Cấu hình CỦA MÁY (đọc từ appsettings.json cạnh file exe) — khác cấu hình của PROJECT
/// (sản phẩm), nằm trong thư mục dữ liệu (Chương 19). Đổi máy thì sửa file này; đổi sản
/// phẩm thì không.
/// </summary>
public sealed class AppSettings
{
    // Thư mục DỮ LIỆU của trạm (Chương 17, mục 17.3). Mặc định "Data" cạnh exe — sản phẩm, job .vpp
    // và cấu hình nằm trong thư mục chương trình như các chương trình tham khảo (DESIGN_LOG [26]),
    // nhưng trong một thư mục con riêng: release.json không băm dữ liệu, chép bản phát hành mới không
    // đè dữ liệu. Tuyệt đối hoặc có biến môi trường (%ProgramData%\MeoVision) cũng được.
    public string DataRoot { get; set; } = "Data";

    // Tên tính năng license VisionPro mà các job cần — đúng chuỗi
    // CogMisc.GetLicensedFeatures trả về (tiền tố "Vx"). Thiếu tính năng nào thì trạm không
    // được chạy tự động. Chương 17, mục 17.4.
    public string[] RequiredVisionFeatures { get; set; } = { "VxPatMax", "VxCaliper", "VxBlob" };

    public int LogRetainedFileCount { get; set; } = 60;     // ~2 tháng log theo ngày

    public int LogFileSizeLimitMb { get; set; } = 100;      // quá cỡ thì cắt sang file mới

    public double MinFreeDiskGb { get; set; } = 20;         // dưới mức này: cảnh báo khởi động

    // Ngôn ngữ giao diện mặc định (lần chạy đầu); người dùng đổi trong menu thì được nhớ
    // lại trong thư mục dữ liệu. Chương 17, mục 17.6.
    public string UiLanguage { get; set; } = "vi-VN";

    // Ngôn ngữ CỐ ĐỊNH của log và file sự cố — kỹ sư hỗ trợ đọc được bất kể máy đặt ngôn
    // ngữ gì. Cũng là ngôn ngữ dự phòng khi bản dịch thiếu khoá.
    public string LogLanguage { get; set; } = "en-US";

    // Số lệnh gần nhất giữ ẢNH trong RAM cho mỗi mã lệnh (khung màn chính + nguồn ảnh của
    // Thiết lập lệnh); 0 = tắt. Ảnh cũ hơn đọc lại từ thư mục ảnh đã lưu. Chương 22, mục 22.3.
    public int RecentFramesPerCommand { get; set; } = 8;

    // Tạo record hiển thị cho mỗi lệnh: 0,6–3 ms/lần tuỳ job, job Blob giữ thêm ~220 MB RAM (đo
    // thật, Chương 22). Tắt khi không ai nhìn màn hình.
    public bool CreateDisplayRecords { get; set; } = true;

    // Số cột của lưới khung ảnh trên màn chính; 0 = tự xếp (tối đa 3 khung một hàng, ô cuối kéo
    // hết hàng). Màn chính luôn mở phóng to; FullScreen = không viền, che cả thanh tác vụ (máy
    // chỉ chạy MeoVision).
    public int DisplayColumns { get; set; }

    public bool FullScreen { get; set; }

    // Kỹ sư/Quản trị tự về Vận hành sau chừng này phút không thao tác; 0 = không tự đăng xuất.
    // Chương 23, mục 23.2.
    public int IdleLogoutMinutes { get; set; } = 10;

    // Thư mục ảnh và CSV kết quả đặt ở ổ khác (ổ dữ liệu lớn, như E:\Image của chương trình tham
    // khảo); rỗng = trong DataRoot. Tuyệt đối, có biến môi trường, hoặc tương đối với thư mục exe.
    public string ImagesRoot { get; set; } = string.Empty;

    public string ResultsRoot { get; set; } = string.Empty;

    // Thư mục log ở ổ khác; rỗng = trong DataRoot. Chương trình + .vpp + cấu hình ở lại thư mục exe
    // (tháo ổ sang máy mới là chạy), ảnh/kết quả/log dồn sang ổ dữ liệu.
    public string LogsRoot { get; set; } = string.Empty;

    // Lưu kết quả và ảnh (Chương 23, mục 23.4).
    public ResultRecorderOptions Results { get; set; } = new();

    // Giữ ảnh/kết quả bao lâu, dọn khi đĩa sắp đầy (Chương 23, mục 23.4.4).
    public RetentionOptions Retention { get; set; } = new();
}
