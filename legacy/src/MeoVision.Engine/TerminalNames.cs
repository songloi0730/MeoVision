namespace MeoVision.Engine;

// Quy ước tên terminal mà file .vpp PHẢI dùng để MeoVision.Engine đọc/ghi đúng — người dựng job
// trong QuickBuild phải đặt tên terminal khớp các hằng số này. Một nơi duy nhất giữ chuỗi tên,
// tránh rải chuỗi "magic string" khắp MapToVisionResult/RunOnce.
internal static class TerminalNames
{
    public const string InputImage = "InputImage";
    public const string Score = "Score";
    public const string PartX = "PartX";
    public const string PartY = "PartY";
    public const string PartAngle = "PartAngle";
    public const string Code = "Code";
}
