namespace MeoVision.Vision;

/// <summary>
/// Khoá chung cho các lời gọi VisionPro đi qua tầng COM của bộ thu ảnh và license (Chương 18,
/// mục 18.5): tạo <c>CogFrameGrabbers</c>, mở/đóng FIFO, đọc <c>CogMisc</c>. Đo thật trên 9.0 CR2:
/// khi những lời gọi đó là lần chạm VisionPro ĐẦU TIÊN của tiến trình và chạy song song trên nhiều
/// luồng, có lần ném <c>ArgumentNullException</c> ("pUnk") từ <c>ccComFactory.CreateInstance</c>
/// (7/40), có lần làm chết cả tiến trình. Tuần tự hoá thì 40/40 đạt. Chỉ khoá các thao tác thưa
/// (mở, đóng, liệt kê) — KHÔNG khoá Acquire, để các camera vẫn chụp song song được.
/// </summary>
internal static class VisionProLock
{
    public static readonly object Gate = new();
}
