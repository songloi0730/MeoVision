using System;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace MeoVision.UI.Maintenance;

/// <summary>
/// Tài khoản Windows đang chạy chương trình có phải quản trị không (Chương 24, mục 24.2). Không chỉ
/// hỏi "đang nâng quyền": tài khoản thuộc nhóm Administrators mà chưa nâng quyền (UAC lọc bớt) vẫn
/// nâng được bằng một cú bấm "Yes" — với người ngồi trước máy, đó vẫn là quản trị.
/// </summary>
public static class WindowsAccount
{
    private const int TokenElevationType = 18;
    private const int ElevationTypeFull = 2;     // đang nâng quyền
    private const int ElevationTypeLimited = 3;  // quản trị bị UAC lọc, nâng được

    public static string UserName => WindowsIdentity.GetCurrent().Name;

    public static bool IsAdministrator()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        if (GetTokenInformation(identity.Token, TokenElevationType, out int type, sizeof(int),
                out _) && type is ElevationTypeFull or ElevationTypeLimited)
        {
            return true;
        }
        // Tắt UAC: kiểu nâng quyền là "Default" cho mọi người — hỏi thẳng vai trò.
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(IntPtr token, int informationClass,
        out int information, int length, out int returnLength);
}
