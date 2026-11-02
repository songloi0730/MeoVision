using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Principal;
using System.Text;
using FluentAssertions;
using MeoVision.App.Hosting;
using MeoVision.App.Maintenance;
using MeoVision.App.Security;
using MeoVision.Core.Localization;
using MeoVision.Core.Security;
using Xunit;

namespace MeoVision.App.Tests;

/// <summary>
/// Chương 24: release.json, các bước kiểm cài đặt, sao lưu/khôi phục trạm, gói chẩn đoán, và lỗ
/// hổng file khôi phục mật khẩu Quản trị.
/// </summary>
public sealed class MaintenanceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mv-maint-" +
        Guid.NewGuid().ToString("N"));
    private readonly string _program;
    private readonly AppPaths _paths;

    public MaintenanceTests()
    {
        _program = Path.Combine(_root, "program");
        Directory.CreateDirectory(Path.Combine(_program, "languages"));
        File.WriteAllText(Path.Combine(_program, "MeoVision.exe"), "exe v1");
        File.WriteAllText(Path.Combine(_program, "MeoVision.App.dll"), "app v1");
        File.WriteAllText(Path.Combine(_program, "languages", "vi-VN.json"), "{}");
        File.WriteAllText(Path.Combine(_program, "appsettings.json"), "{ \"a\": 1 }");
        _paths = AppPaths.Resolve(Path.Combine(_root, "data"), _root);
        _paths.EnsureCreated();
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    // ---------------- release.json ----------------

    [Fact]
    public void Release_Manifest_Finds_Missing_Replaced_And_Foreign_Files_But_Not_Settings()
    {
        ReleaseInfo release = ReleaseManifest.Write(_program, "2.0.261029", "9.0 CR2");
        release.Files.Keys.Should().Equal("languages/vi-VN.json", "MeoVision.App.dll",
            "MeoVision.exe");
        ReleaseManifest.Verify(_program, release).Should().BeEmpty();

        File.WriteAllText(Path.Combine(_program, "appsettings.json"), "{ \"a\": 2 }");  // máy sửa
        File.WriteAllText(Path.Combine(_program, "MeoVision.App.dll"), "app hotfix");   // chép đè
        File.Delete(Path.Combine(_program, "languages", "vi-VN.json"));                // chép thiếu
        File.WriteAllText(Path.Combine(_program, "Old.Plugin.dll"), "x");              // chép lẫn

        IReadOnlyList<Msg> problems = ReleaseManifest.Verify(_program,
            ReleaseManifest.Read(_program)!);

        problems.Select(p => p.Key + ":" + p.Args[0]).Should().BeEquivalentTo(
            "release.missing:languages/vi-VN.json", "release.changed:MeoVision.App.dll",
            "release.extra:Old.Plugin.dll");
    }

    [Fact]
    public void Program_Files_Step_Warns_On_Build_Folder_Problems_And_Other_VisionPro()
    {
        InstallChecks.ProgramFiles(_program, null, "9.0 CR2").Message.Key
            .Should().Be("release.none");
        ReleaseInfo release = ReleaseManifest.Write(_program, "2.0.261029", "9.0 CR2");
        InstallChecks.ProgramFiles(_program, release, "9.0 CR2").Status
            .Should().Be(StepStatus.Ok);
        StepOutcome other = InstallChecks.ProgramFiles(_program, release, "9.6");
        other.Status.Should().Be(StepStatus.Warning);
        other.Message.Key.Should().Be("release.visionProDiffers");
        InstallChecks.Account("IPC-01\\operator", isAdministrator: true).Status
            .Should().Be(StepStatus.Warning);
    }

    [Fact]
    public void Check_Exit_Code_Is_0_Ready_1_Warnings_2_Blocked()
    {
        StepResult ok = new("a", true, StepStatus.Ok, Msg.Empty, 1);
        StepResult warn = new("b", false, StepStatus.Warning, Msg.Empty, 1);
        StepResult failed = new("c", true, StepStatus.Failed, Msg.Empty, 1);

        InstallChecks.ExitCode(new StartupReport(new[] { ok })).Should().Be(0);
        InstallChecks.ExitCode(new StartupReport(new[] { ok, warn })).Should().Be(1);
        InstallChecks.ExitCode(new StartupReport(new[] { ok, warn, failed })).Should().Be(2);
    }

    // ---------------- sao lưu / khôi phục trạm ----------------

    private void WriteStation(string marker)
    {
        File.WriteAllText(_paths.MachineFile, "{ \"machine\": \"" + marker + "\" }");
        Directory.CreateDirectory(_paths.MachineCalibration);
        File.WriteAllText(Path.Combine(_paths.MachineCalibration, "top.vpp"), "calib " + marker);
        Directory.CreateDirectory(_paths.Product("P-1").Jobs);
        File.WriteAllText(_paths.Product("P-1").ProductFile, "{ \"id\": \"P-1\" }");
        File.WriteAllText(_paths.Product("P-1").Job("top.vpp"), "job " + marker);
        File.WriteAllText(_paths.ActiveProductFile, "P-1");
        File.WriteAllText(_paths.UsersFile, "{ \"users\": [] }");
        // Dữ liệu sản xuất — KHÔNG được vào bản sao lưu.
        Directory.CreateDirectory(Path.Combine(_paths.Images, "2026-10-28"));
        File.WriteAllText(Path.Combine(_paths.Images, "2026-10-28", "a_NG.bmp"), "img");
        // Giờ sửa file cố định: test không được phụ thuộc giờ thật lúc chạy.
        File.SetLastWriteTime(Path.Combine(_paths.Images, "2026-10-28", "a_NG.bmp"),
            new DateTime(2026, 10, 28, 8, 0, 0));
        File.WriteAllText(Path.Combine(_paths.Logs, "meovision-20261028.log"), "log");
    }

    [Fact]
    public void Station_Backup_Holds_Configuration_Not_Production_Data()
    {
        WriteStation("A");
        string zip = Path.Combine(_root, "station.zip");

        StationBackupInfo info = new StationBackup(_paths, _program).Create(zip, "admin",
            "2.0.261029");

        info.Files.Keys.Should().BeEquivalentTo("data/machine.json",
            "data/machine/calibration/top.vpp", "data/products/P-1/product.json",
            "data/products/P-1/jobs/top.vpp", "data/active-product.txt", "data/users.json",
            "program/appsettings.json");
        using ZipArchive archive = ZipFile.OpenRead(zip);
        archive.Entries.Select(e => e.FullName).Should().Contain(StationBackup.ManifestEntry)
            .And.NotContain(e => e.Contains("images") || e.Contains("logs"));
    }

    [Fact]
    public void Restore_On_A_New_Machine_Gives_The_Same_Bytes_And_Keeps_The_Old_Config()
    {
        WriteStation("A");
        string zip = Path.Combine(_root, "station.zip");
        new StationBackup(_paths, _program).Create(zip, "admin", "2.0.261029");
        AppPaths fresh = AppPaths.Resolve(Path.Combine(_root, "new-pc"), _root);
        fresh.EnsureCreated();
        File.WriteAllText(fresh.MachineFile, "{ \"machine\": \"fresh\" }");   // cấu hình mặc định

        StationRestoreResult result = new StationBackup(fresh, _program).Restore(zip);

        result.Ok.Should().BeTrue();
        File.ReadAllText(fresh.MachineFile).Should().Contain("\"A\"");
        File.ReadAllText(fresh.Product("P-1").Job("top.vpp")).Should().Be("job A");
        File.ReadAllText(Path.Combine(fresh.Root, StationBackup.RestoredSettingsFile))
            .Should().Be("{ \"a\": 1 }");
        File.ReadAllText(Path.Combine(result.MovedAsideTo!, "machine.json"))
            .Should().Contain("fresh");
        AppPaths empty = AppPaths.Resolve(Path.Combine(_root, "empty-pc"), _root);
        empty.EnsureCreated();
        new StationBackup(empty, _program).Restore(zip).MovedAsideTo.Should().BeNull();
    }

    [Fact]
    public void Restore_Refuses_A_Damaged_Backup_And_A_Zip_Slip_Without_Writing_Anything()
    {
        WriteStation("A");
        string zip = Path.Combine(_root, "station.zip");
        new StationBackup(_paths, _program).Create(zip, "admin", "2.0.261029");
        using (ZipArchive archive = ZipFile.Open(zip, ZipArchiveMode.Update))
        {
            archive.GetEntry("data/products/P-1/jobs/top.vpp")!.Delete();
            using (StreamWriter w = new(archive.CreateEntry("data/products/P-1/jobs/top.vpp")
                       .Open()))
            {
                w.Write("job edited by hand");
            }
            ZipArchiveEntry manifest = archive.GetEntry(StationBackup.ManifestEntry)!;
            string json;
            using (StreamReader r = new(manifest.Open()))
            {
                json = r.ReadToEnd();
            }
            manifest.Delete();
            using StreamWriter m = new(archive.CreateEntry(StationBackup.ManifestEntry).Open());
            m.Write(json.Replace("\"data/users.json\"", "\"data/../../evil.txt\""));
        }
        File.WriteAllText(_paths.MachineFile, "current");

        StationRestoreResult result = new StationBackup(_paths, _program).Restore(zip);

        result.Ok.Should().BeFalse();
        result.Problems.Select(p => p.Key).Should().BeEquivalentTo("station.restore.changed",
            "station.restore.badPath");
        File.ReadAllText(_paths.MachineFile).Should().Be("current");
        File.Exists(Path.Combine(_root, "evil.txt")).Should().BeFalse();
    }

    // ---------------- gói chẩn đoán ----------------

    [Fact]
    public void Diagnostic_Package_Is_Narrow_Reads_The_Open_Log_And_Never_Has_Passwords()
    {
        WriteStation("A");
        Directory.CreateDirectory(_paths.Product("P-2").Jobs);
        File.WriteAllText(_paths.Product("P-2").ProductFile, "{ \"id\": \"P-2\" }");
        DateTime now = new(2026, 10, 29, 9, 0, 0);
        string today = Path.Combine(_paths.Images, "2026-10-29", "T3");
        Directory.CreateDirectory(today);
        for (int i = 0; i < 5; i++)
        {
            string ng = Path.Combine(today, $"0900{i:00}_000_SN{i}_NG.bmp");
            File.WriteAllText(ng, "ng" + i);
            File.SetLastWriteTime(ng, now.AddMinutes(-10 + i));
        }
        File.WriteAllText(Path.Combine(today, "090010_000_SN9_OK.bmp"), "ok");
        string log = Path.Combine(_paths.Logs, "meovision-20261029.log");
        // Như Serilog: file log hôm nay đang mở để ghi.
        using FileStream open = new(log, FileMode.Create, FileAccess.Write, FileShare.Read);
        open.Write(new byte[] { 65, 66 }, 0, 2);
        open.Flush();

        DiagnosticBundleResult result = new DiagnosticBundle(_paths, _program).Export(
            "máy kêu lạ trước khi lỗi", "system", "P-1",
            new DiagnosticOptions { Days = 2, MaxImages = 2 }, now);

        using ZipArchive archive = ZipFile.OpenRead(result.File);
        string[] names = archive.Entries.Select(e => e.FullName).ToArray();
        names.Should().Contain(new[]
        {
            "note.txt", "system.txt", "machine.json", "appsettings.json",
            "products/P-1/product.json", "logs/meovision-20261029.log",
            "images/2026-10-29/T3/090004_000_SN4_NG.bmp",
            "images/2026-10-29/T3/090003_000_SN3_NG.bmp",
        });
        names.Should().NotContain(n => n.Contains("users") || n.Contains("P-2") ||
                                       n.EndsWith("_OK.bmp") || n.Contains("SN2_NG"));
    }

    // ---------------- file khôi phục (sửa ở Chương 24) ----------------

    [Fact]
    public void Recovery_File_From_A_Standard_Account_Is_Rejected_And_Deleted()
    {
        string users = Path.Combine(_paths.Root, "users.json");
        string recovery = Path.Combine(_paths.Root, UserStore.RecoveryFileName);
        UserStore store = new(users, recovery, _ => null);   // chủ file không được tin
        store.Load();
        File.WriteAllText(recovery, "taken-over\r\n");

        RecoveryResult result = store.ApplyRecovery();

        result.Outcome.Should().Be(RecoveryOutcome.Untrusted);
        File.Exists(recovery).Should().BeFalse();
        store.Verify("admin", "taken-over").Should().BeNull();
        store.Verify("admin", "admin").Should().Be(UserRole.Administrator);
    }

    [Fact]
    public void A_File_Made_By_This_Test_Is_Not_Owned_By_Administrators_Unless_Elevated()
    {
        string file = Path.Combine(_paths.Root, "owner.txt");
        File.WriteAllText(file, "x");
        bool elevated = new WindowsPrincipal(WindowsIdentity.GetCurrent())
            .IsInRole(WindowsBuiltInRole.Administrator);

        string? owner = UserStore.AdministratorsOwner(file);

        // Đo thật trên NTFS: phiên không nâng quyền tạo file → chủ là chính người dùng → từ chối.
        if (elevated)
        {
            owner.Should().NotBeNull();
        }
        else
        {
            owner.Should().BeNull();
        }
    }
}
