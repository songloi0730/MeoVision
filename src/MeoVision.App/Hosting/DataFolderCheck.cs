using System;
using System.IO;
using MeoVision.Core;
using MeoVision.Core.Localization;

namespace MeoVision.App.Hosting;

/// <summary>
/// Bước khởi động đầu tiên (Chương 17, mục 17.3): thư mục dữ liệu GHI ĐƯỢC và ổ đĩa còn
/// chỗ. "Tạo được thư mục" chưa đủ — quyền ghi bị chặn (chính sách IT) hay ổ đầy là hai lý
/// do phổ biến khiến ảnh NG và log biến mất lặng lẽ giữa ca.
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
            return StepOutcome.Failed(Msg.Of("startup.dataFolder.notWritable", paths.Root,
                ex.Message));
        }

        if (paths.FallBacks.Count > 0)
        {
            return StepOutcome.Warning(paths.FallBacks[0]);
        }

        // Lỗi thật 2026-10-30: dữ liệu nằm trong OneDrive, OneDrive giữ thư mục bản nháp cũ
        // (ReadOnly + ReparsePoint) và Thiết lập lệnh không mở được. Máy sản xuất không bao giờ
        // nên để dữ liệu trong thư mục đồng bộ đám mây.
        string? synced = CloudSyncedRoot(paths.Root);
        if (synced is not null)
        {
            return StepOutcome.Warning(Msg.Of("startup.dataFolder.cloudSynced", paths.Root,
                synced));
        }

        DriveInfo drive = new(Path.GetPathRoot(paths.Root)!);
        double freeGb = drive.AvailableFreeSpace / 1024d / 1024d / 1024d;
        return freeGb < minFreeGb
            ? StepOutcome.Warning(Msg.Of("startup.dataFolder.lowDisk", drive.Name, freeGb,
                minFreeGb))
            : StepOutcome.Ok(Msg.Of("startup.dataFolder.ok", paths.Root, freeGb));
    }

    /// <summary>Thư mục OneDrive chứa <paramref name="folder"/>, hoặc null.</summary>
    public static string? CloudSyncedRoot(string folder)
    {
        Guard.ThrowIfNull(folder, nameof(folder));
        string full = Path.GetFullPath(folder).TrimEnd('\\') + "\\";
        foreach (string name in new[] { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" })
        {
            string? root = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrEmpty(root) &&
                full.StartsWith(root!.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
            {
                return root;
            }
        }
        return null;
    }
}
