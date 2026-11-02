using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MeoVision.Core;
using MeoVision.Core.Localization;

namespace MeoVision.App.Hosting;

/// <summary>
/// Hai bước khởi động thêm ở Chương 24 (mục 24.2): file chương trình có đúng bản phát hành không,
/// và chương trình có đang chạy bằng quyền quá lớn không. Chạy ở MỌI lần khởi động, không chỉ khi
/// <c>--check</c> — cài hỏng sau một lần "sửa nóng" cũng phải lộ ra.
/// </summary>
public static class InstallChecks
{
    /// <summary>Phiên bản hiển thị: <c>InformationalVersion</c> (2.0.YYMMDD) của exe.</summary>
    public static string AppVersion(Assembly assembly)
    {
        Guard.ThrowIfNull(assembly, nameof(assembly));
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? assembly.GetName().Version?.ToString() ?? "?";
    }

    /// <param name="release">Null = không có release.json (chạy từ thư mục build).</param>
    /// <param name="installedVisionPro">Bản VisionPro đang cài, đúng chuỗi của bước
    /// "VisionPro".</param>
    public static StepOutcome ProgramFiles(string programDirectory, ReleaseInfo? release,
        string installedVisionPro)
    {
        Guard.ThrowIfNull(programDirectory, nameof(programDirectory));
        if (release is null)
        {
            return StepOutcome.Warning(Msg.Of("release.none", ReleaseManifest.FileName));
        }
        IReadOnlyList<Msg> problems = ReleaseManifest.Verify(programDirectory, release);
        if (problems.Count > 0)
        {
            return StepOutcome.Warning(Msg.Of("release.problems", release.Version, problems.Count,
                problems[0]));
        }
        // Khác bản VisionPro lúc build: chạy được nhờ chuyển hướng assembly hoặc chưa chạm tới kiểu
        // khác biệt — Ch13 §13.1.3. Cảnh báo để người cài biết đang chạy ngoài cấu hình đã thử.
        return release.VisionPro != installedVisionPro
            ? StepOutcome.Warning(Msg.Of("release.visionProDiffers", release.VisionPro,
                installedVisionPro))
            : StepOutcome.Ok(Msg.Of("release.ok", release.Version, release.Files.Count));
    }

    /// <summary>
    /// Chạy bằng tài khoản quản trị Windows: mọi người ngồi trước máy có quyền sửa cả chương trình,
    /// tạo file khôi phục, đổi cấu hình — mật khẩu của MeoVision không còn ý nghĩa.
    /// </summary>
    public static StepOutcome Account(string userName, bool isAdministrator) =>
        isAdministrator
            ? StepOutcome.Warning(Msg.Of("startup.account.admin", userName))
            : StepOutcome.Ok(Msg.Of("startup.account.ok", userName));

    /// <summary>Mã thoát của <c>--check</c>: 0 sẵn sàng, 1 có cảnh báo, 2 bị chặn.</summary>
    public static int ExitCode(StartupReport report)
    {
        Guard.ThrowIfNull(report, nameof(report));
        if (!report.CanRunProduction)
        {
            return 2;
        }
        return report.Steps.Any(s => s.Status != StepStatus.Ok) ? 1 : 0;
    }
}
