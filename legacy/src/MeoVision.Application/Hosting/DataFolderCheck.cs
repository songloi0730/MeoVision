using System;
using System.IO;
using MeoVision.Core;

namespace MeoVision.Application.Hosting;

/// <summary>
/// Bước khởi động đầu tiên (Chương 17, mục 17.3): thư mục dữ liệu GHI ĐƯỢC và ổ đĩa còn chỗ.
/// "Tạo được thư mục" chưa đủ — quyền ghi bị chặn (chính sách IT) hay ổ đầy là hai lý do phổ biến
/// khiến ảnh NG và log biến mất lặng lẽ giữa ca.
/// </summary>
public static class DataFolderCheck
{
    public static StepOutcome Run(AppPaths paths, double minFreeGb)
    {
        Guard.ThrowIfNull(paths, nameof(paths));

        string probe = Path.Combine(paths.Root, ".write-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            return StepOutcome.Failed($"khong ghi duoc vao {paths.Root}: {ex.Message}");
        }

        DriveInfo drive = new(Path.GetPathRoot(paths.Root)!);
        double freeGb = drive.AvailableFreeSpace / 1024d / 1024d / 1024d;
        return freeGb < minFreeGb
            ? StepOutcome.Warning($"o {drive.Name} chi con {freeGb:0.0} GB (< {minFreeGb} GB)")
            : StepOutcome.Ok($"{paths.Root} — trong {freeGb:0.0} GB");
    }
}
