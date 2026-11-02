using System;
using System.Collections.Generic;

namespace MeoVision.Core.Security;

/// <summary>
/// Ba vai trò (UI_Spec v2.2 §6; tác giả chốt bỏ Trưởng ca): Vận hành không cần mật khẩu, Kỹ sư và
/// Quản trị phải đăng nhập. Thứ tự số có nghĩa: vai trò lớn hơn làm được mọi việc của vai trò nhỏ
/// hơn.
/// </summary>
public enum UserRole
{
    Operator = 0,
    Engineer = 1,
    Administrator = 2,
}

/// <summary>Những việc cần phân quyền (Chương 23, mục 23.2).</summary>
public enum Permission
{
    ResetCounters,
    ViewHistory,
    SwitchProduct,
    TestGrab,
    CommandSetup,
    ToggleOnline,
    ExitApplication,
    ManageUsers,
    ExportDiagnostics,
    BackupStation,
    StationSettings,
    Bypass,
}

/// <summary>
/// Bảng quyền — MỘT chỗ duy nhất. Nút trên giao diện, lệnh của máy và test đều hỏi ở đây, không ai
/// tự viết <c>if (role == ...)</c> riêng.
/// </summary>
public static class Permissions
{
    private static readonly Dictionary<Permission, UserRole> Minimum = new()
    {
        [Permission.ResetCounters] = UserRole.Operator,
        [Permission.ViewHistory] = UserRole.Operator,
        [Permission.SwitchProduct] = UserRole.Engineer,
        [Permission.TestGrab] = UserRole.Engineer,
        [Permission.CommandSetup] = UserRole.Engineer,
        [Permission.ToggleOnline] = UserRole.Engineer,
        [Permission.ExitApplication] = UserRole.Engineer,
        [Permission.ManageUsers] = UserRole.Administrator,
        // Chương 24: người thấy sự cố là người xuất gói; bản sao lưu chứa mã băm mật khẩu.
        [Permission.ExportDiagnostics] = UserRole.Operator,
        [Permission.BackupStation] = UserRole.Administrator,
        // DESIGN_LOG [27]: thư mục ảnh/log, ngưỡng dọn đĩa — sai là mất ảnh, nên chỉ Quản trị.
        [Permission.StationSettings] = UserRole.Administrator,
        // DESIGN_LOG [28] D1: người chịu trách nhiệm kỹ thuật cho việc chạy không kiểm.
        [Permission.Bypass] = UserRole.Engineer,
    };

    public static UserRole Required(Permission permission) =>
        Minimum.TryGetValue(permission, out UserRole role)
            ? role
            : throw new ArgumentOutOfRangeException(nameof(permission), permission, null);

    public static bool Allows(UserRole role, Permission permission) => role >= Required(permission);
}
