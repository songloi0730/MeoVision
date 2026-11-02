namespace MeoVision.Application.Auth;

// Chỉ 2 cấp — đủ để gác màn Teach (Ch14 §14.4.2), không cần 5 cấp như AM.AutoFrame (dự án điều
// khiển máy thật, khác mục tiêu code-sách của MeoVision — xem DESIGN_LOG.md mục [8]).
public enum UserLevel
{
    Operator,
    Engineer
}
