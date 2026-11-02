namespace MeoVision.Core.Contracts;

// Đặt tên khớp đúng CogAcqTriggerModelConstants thật (Ch6 Bảng 6.2) — bớt "Slave" (dùng khi đồng bộ
// nhiều camera cùng lúc, ngoài phạm vi 1 camera của MeoVision).
public enum CameraTriggerMode
{
    FreeRun,
    Auto,
    Semi,
    Manual
}
