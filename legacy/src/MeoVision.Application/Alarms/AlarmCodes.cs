namespace MeoVision.Application.Alarms;

// Dải phẳng — MeoVision chỉ có đúng 1 "thiết bị" (vision), không cần bảng dải mã nhiều loại như
// AlarmCodes.cs của AM.AutoFrame (Motion/Vision/IO/System... mỗi loại 1 dải 10000-90000).
public static class AlarmCodes
{
    public const int JobLoadFailed = 1001;
    public const int InspectionTimeout = 1002;
    public const int EngineFault = 1003;
    public const int RecipeInvalid = 1004;
    public const int NgImageStorageNearFull = 1005;
}
