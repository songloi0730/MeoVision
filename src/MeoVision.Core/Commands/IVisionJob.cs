using System;
using System.Collections.Generic;
using MeoVision.Core.Devices;

namespace MeoVision.Core.Commands;

/// <summary>
/// Một job ĐÃ nạp và ĐÃ kiểm, giữ suốt thời gian sản phẩm chạy (Chương 20, mục 20.2). Đối tượng
/// chạy chính là đối tượng đã kiểm lúc nạp — không mở file lần hai.
/// </summary>
public interface IVisionJob : IDisposable
{
    /// <summary>Tên file job, ví dụ "T1_mean.vpp".</summary>
    string Name { get; }

    /// <summary>
    /// Chạy job trên một ảnh và đọc các output được hỏi. Mỗi job chạy TỪNG lệnh một — hai luồng
    /// gọi cùng lúc thì luồng sau chờ. Không huỷ được giữa chừng: VisionPro không có cách dừng
    /// một ToolBlock đang chạy.
    /// </summary>
    JobRun Run(CameraImage image, IReadOnlyList<string> outputs);

    /// <summary>
    /// Chạy đầy đủ (Chương 22): đặt giá trị terminal input (tham số nhanh của lệnh) trước khi chạy,
    /// và tạo record hiển thị ngay trong lần chạy — record phải tạo khi ảnh còn sống.
    /// </summary>
    JobRun Run(CameraImage image, JobRequest request);

    /// <summary>Ghi job ra file (file tạm + đổi tên) — áp dụng job nháp đã sửa.</summary>
    void Save(string file);

    /// <summary>
    /// Đối tượng cho editor gốc của VisionPro (UI ép về <c>CogToolBlock</c>). Chỉ đưa ra job của
    /// BẢN NHÁP — không bao giờ đưa job đang trả lời máy vào editor.
    /// </summary>
    object EditSubject { get; }
}

/// <summary>
/// Một lần chạy: output cần đọc, giá trị đặt vào terminal input (theo tên), có tạo record không.
/// </summary>
public sealed record JobRequest(IReadOnlyList<string> Outputs,
    IReadOnlyDictionary<string, double> Inputs, bool WithRecord,
    IReadOnlyDictionary<string, CameraImage>? Images = null);

/// <summary>Kết luận của chính job (RunStatus của ToolBlock), chưa xét giới hạn của lệnh.</summary>
public enum JobRunStatus
{
    Accept,
    Warning,
    Reject,
    Error,
}

/// <summary>
/// Kết quả một lần chạy job. <see cref="Outputs"/> chứa giá trị THÔ theo tên terminal; null =
/// job không gán giá trị ở lần chạy này. <see cref="Record"/>: record hiển thị (Cognex), nếu có.
/// </summary>
public sealed record JobRun(
    JobRunStatus Status,
    string Message,
    IReadOnlyDictionary<string, object?> Outputs,
    long ElapsedMs,
    object? Record = null);
