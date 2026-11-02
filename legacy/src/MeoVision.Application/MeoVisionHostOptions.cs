namespace MeoVision.Application;

public sealed class MeoVisionHostOptions
{
    public bool UseSimulatedEngine { get; set; } = true;

    public string RecipesDirectory { get; set; } = "Recipes";

    public string ActiveRecipeId { get; set; } = "default";

    public string NgImageDirectory { get; set; } = "NgImages";

    public int NgImageRetentionDays { get; set; } = 90;

    public long NgImageMaxBytes { get; set; } = 5_368_709_120; // 5 GB

    public string LogDirectory { get; set; } = "Logs";

    // ── Chương 17: khung dự án & vòng đời ──
    // Thư mục DỮ LIỆU của trạm (project, log, ảnh, kết quả, backup...). Tuyệt đối hoặc có biến môi
    // trường; tương đối thì tính từ thư mục chương trình (chỉ nên dùng trên máy dev).
    public string DataRoot { get; set; } = @"%ProgramData%\MeoVision";

    // Tên tính năng license VisionPro mà các job cần — đúng chuỗi do CogMisc.GetLicensedFeatures
    // trả về (tiền tố "Vx"). Thiếu tính năng nào thì trạm không được chạy tự động.
    public string[] RequiredVisionFeatures { get; set; } = { "VxPatMax", "VxCaliper", "VxBlob" };

    public int LogRetainedFileCount { get; set; } = 60;        // ~2 tháng log theo ngày

    public int LogFileSizeLimitMb { get; set; } = 100;         // quá cỡ thì cắt sang file mới

    public double MinFreeDiskGb { get; set; } = 20;            // dưới mức này: cảnh báo khởi động

    // Đọc từ appsettings.json, KHÔNG hardcode trong source — chỉ gác màn Teach (Ch14 §14.4.2),
    // không phải cơ chế bảo mật thật (xem DESIGN_LOG.md mục [8]: UserLevel tối giản, không có
    // IUserService/database user đầy đủ).
    public string EngineerPassword { get; set; } = "engineer123";
}
