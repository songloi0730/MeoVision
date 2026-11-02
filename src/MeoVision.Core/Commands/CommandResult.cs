using System;
using System.Collections.Generic;
using MeoVision.Core.Localization;

namespace MeoVision.Core.Commands;

/// <summary>
/// Kết luận gửi về máy (Chương 20, mục 20.5). NG và LỖI là hai chuyện khác nhau: NG = đã nhìn
/// được và sản phẩm sai; Error = KHÔNG kết luận được (chụp hỏng, job lỗi, quá giờ) — máy phải
/// xử lý khác, không được coi Error là NG rồi loại sản phẩm tốt.
/// </summary>
public enum CommandStatus
{
    Ok,
    Ng,
    Error,

    /// <summary>
    /// Không kiểm — lệnh đang được Kỹ sư cho bỏ qua, có lý do và có hạn (DESIGN_LOG [28] D1). KHÔNG phải
    /// OK: máy biết sản phẩm này chưa qua vision.
    /// </summary>
    Bypassed,
}

/// <summary>
/// Một giá trị trả về đã xử lý: đã cộng bù, đã kiểm giới hạn, đã định dạng theo văn hoá bất biến.
/// <see cref="Value"/> null = không có số (job không gán, hoặc output không phải số).
/// </summary>
public sealed record OutputValue(string Name, double? Value, string Text, bool InLimits);

/// <summary>Thời gian từng bước của một lệnh, ms — để biết chậm ở đâu khi máy than chu kỳ dài.</summary>
public sealed record StepTimes(
    long QueueMs,
    long LightMs,
    long GrabMs,
    long CalibrationMs,
    long RunMs,
    long TotalMs,
    int GrabAttempts)
{
    public static readonly StepTimes None = new(0, 0, 0, 0, 0, 0, 0);
}

/// <summary>Kết quả một lệnh — thứ duy nhất lớp giao tiếp (Chương 21) và giao diện (22) nhận.</summary>
public sealed record CommandResult(
    string Code,
    CommandStatus Status,
    Msg? Error,
    IReadOnlyList<OutputValue> Outputs,
    StepTimes Times)
{
    /// <summary>
    /// Ảnh + record của lần chạy (Chương 22). Bộ máy chạy lệnh trả bản của nó ngay sau sự kiện
    /// <c>Completed</c>; ai muốn giữ thì gọi <see cref="DisplayFrame.Share"/> trong lúc xử lý sự
    /// kiện.
    /// </summary>
    public DisplayFrame? Frame { get; init; }

    /// <summary>
    /// Truy vết (Chương 23): kênh, serial máy gửi kèm, lúc nhận, sản phẩm và PHIÊN BẢN sản phẩm (mã
    /// băm nội dung lúc nạp) đã trả lời lệnh này. Khách hàng khiếu nại một serial thì biết ngay
    /// lệnh chạy với job và giới hạn nào.
    /// </summary>
    public string Channel { get; init; } = string.Empty;

    public string Serial { get; init; } = string.Empty;

    public DateTime ReceivedAt { get; init; }

    public string ProductId { get; init; } = string.Empty;

    public string ProductVersion { get; init; } = string.Empty;

    public static CommandResult Failed(string code, Msg error, StepTimes times) =>
        new(code, CommandStatus.Error, error, Array.Empty<OutputValue>(), times);

    public static CommandResult Bypassed(string code, Msg reason, StepTimes times) =>
        new(code, CommandStatus.Bypassed, reason, Array.Empty<OutputValue>(), times);
}
