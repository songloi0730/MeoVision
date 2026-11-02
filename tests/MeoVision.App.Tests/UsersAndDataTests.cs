using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using MeoVision.App.Audit;
using MeoVision.App.Commands;
using MeoVision.App.Devices;
using MeoVision.App.Hosting;
using MeoVision.App.Products;
using MeoVision.App.Results;
using MeoVision.App.Security;
using MeoVision.Core.Commands;
using MeoVision.Core.Devices;
using MeoVision.Core.Security;
using MeoVision.Core.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MeoVision.App.Tests;

/// <summary>
/// Chương 23: tài khoản (không khoá, khôi phục quyền Quản trị), phiên làm việc (tự đăng xuất, bảng
/// quyền), nhật ký thay đổi, ghi/đọc kết quả + ảnh, dọn đĩa, phiên bản sản phẩm.
/// </summary>
public sealed class UsersAndDataTests : IDisposable
{
    private const string Product = @"{
  ""schemaVersion"": 1, ""id"": ""P-1"", ""name"": ""Data test"",
  ""commands"": [
    { ""code"": ""T1"", ""name"": ""Top"", ""channel"": ""CH1"", ""camera"": ""TopCam"",
      ""job"": ""top.vpp"", ""timeoutMs"": 1000,
      ""outputs"": [ { ""terminal"": ""X"", ""name"": ""Spot X"", ""format"": ""0.0"",
                       ""lower"": 0, ""upper"": 15 } ] }
  ]
}";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "mv-data-" +
        Guid.NewGuid().ToString("N"));
    private readonly AppPaths _paths;
    private DateTime _now = new(2026, 10, 28, 9, 0, 0);

    public UsersAndDataTests()
    {
        _paths = AppPaths.Resolve(_root, Path.GetTempPath());
        _paths.EnsureCreated();
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    // ---------------- tài khoản ----------------

    [Fact]
    public void First_Run_Creates_Factory_Accounts_Without_Storing_Passwords()
    {
        UserStore users = Users();
        users.Load();

        users.Accounts.Select(a => (a.Name, a.Role)).Should().Equal(("engineer", UserRole.Engineer),
            ("admin", UserRole.Administrator));
        users.Accounts.Should().OnlyContain(a => a.IsDefaultPassword);
        string json = File.ReadAllText(_paths.UsersFile);
        json.Should().NotContain("password").And.Contain("\"hash\"").And.Contain("\"Engineer\"");
        users.Verify("engineer", "engineer").Should().Be(UserRole.Engineer);
        users.Verify("engineer", "wrong").Should().BeNull();
    }

    [Fact]
    public void Wrong_Passwords_Never_Lock_The_Account()
    {
        UserStore users = Users();
        users.Load();
        for (int i = 0; i < 20; i++)
        {
            users.Verify("admin", "guess" + i).Should().BeNull();
        }

        users.Verify("admin", "admin").Should().Be(UserRole.Administrator);   // 2 giờ sáng vẫn vào được
    }

    [Fact]
    public void Changed_Password_Survives_Restart_And_Clears_The_Factory_Flag()
    {
        UserStore users = Users();
        users.Load();
        users.SetPassword("engineer", "line-7-eng");

        UserStore again = Users();
        again.Load();
        again.Verify("engineer", "line-7-eng").Should().Be(UserRole.Engineer);
        again.Verify("engineer", "engineer").Should().BeNull();
        again.Accounts.Single(a => a.Name == "engineer").IsDefaultPassword.Should().BeFalse();
        users.Invoking(u => u.SetPassword("engineer", "123")).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Recovery_File_Resets_Administrators_Once_And_Is_Deleted()
    {
        UserStore users = Users();
        users.Load();
        users.ApplyRecovery().Outcome.Should().Be(RecoveryOutcome.None);   // không có file
        File.WriteAllText(RecoveryFile(), "new-admin-2026\r\n");

        RecoveryResult reset = users.ApplyRecovery();

        reset.Outcome.Should().Be(RecoveryOutcome.Applied);
        reset.Admins.Should().Equal("admin");
        File.Exists(RecoveryFile()).Should().BeFalse();
        users.Verify("admin", "new-admin-2026").Should().Be(UserRole.Administrator);
        File.WriteAllText(RecoveryFile(), "abc");   // quá ngắn: xoá file, không đổi gì
        users.ApplyRecovery().Outcome.Should().Be(RecoveryOutcome.Invalid);
        File.Exists(RecoveryFile()).Should().BeFalse();
        users.Verify("admin", "new-admin-2026").Should().Be(UserRole.Administrator);
    }

    // ---------------- phiên làm việc + quyền ----------------

    [Fact]
    public void Session_Starts_As_Operator_Logs_In_And_Falls_Back_After_Idle_Time()
    {
        AuditLog audit = new(_paths.Audit);
        Session session = NewSession(audit);
        session.Role.Should().Be(UserRole.Operator);
        session.Can(Permission.CommandSetup).Should().BeFalse();
        session.Can(Permission.ViewHistory).Should().BeTrue();

        session.Login("engineer", "nope").Should().BeFalse();
        session.Login("engineer", "engineer").Should().BeTrue();
        session.Can(Permission.CommandSetup).Should().BeTrue();
        session.Can(Permission.ManageUsers).Should().BeFalse();
        _now = _now.AddMinutes(9);
        session.Touch();                                   // còn thao tác: đồng hồ chạy lại
        _now = _now.AddMinutes(9);
        session.Tick();
        session.Role.Should().Be(UserRole.Engineer);
        session.RemainingBeforeLogout.Should().Be(TimeSpan.FromMinutes(1));
        _now = _now.AddMinutes(1);
        session.Tick();

        session.Role.Should().Be(UserRole.Operator);
        audit.Read(DateTime.Now.AddMinutes(-5), DateTime.Now.AddMinutes(1))
            .Select(e => (e.Action, e.Detail)).Should().Equal(("login-failed", ""),
                ("login", "Engineer"), ("logout", "idle"));
    }

    [Theory]
    [InlineData(UserRole.Operator, Permission.ResetCounters, true)]
    [InlineData(UserRole.Operator, Permission.ToggleOnline, false)]
    [InlineData(UserRole.Operator, Permission.ExitApplication, false)]
    [InlineData(UserRole.Engineer, Permission.SwitchProduct, true)]
    [InlineData(UserRole.Engineer, Permission.ManageUsers, false)]
    [InlineData(UserRole.Administrator, Permission.ManageUsers, true)]
    public void Permission_Table_Matches_The_Spec(UserRole role, Permission permission, bool allowed)
    {
        Permissions.Allows(role, permission).Should().Be(allowed);
    }

    // ---------------- nhật ký thay đổi ----------------

    [Fact]
    public void Audit_Log_Is_Json_Lines_And_Skips_A_Broken_Line()
    {
        AuditLog audit = new(_paths.Audit);
        audit.Write("engineer", "Engineer", "apply", "P-1", "raise min area",
            new Dictionary<string, string> { ["version"] = "abc123", ["changes"] = "a, \"b\"" });
        string file = Directory.GetFiles(_paths.Audit, "*.jsonl").Single();
        File.AppendAllText(file, "{ broken\n");
        audit.Write("operator", "Operator", "online", "P-1", "button");

        IReadOnlyList<AuditEntry> entries = audit.Read(DateTime.Now.AddMinutes(-1),
            DateTime.Now.AddMinutes(1));

        entries.Select(e => e.Action).Should().Equal("apply", "online");
        entries[0].Data["changes"].Should().Be("a, \"b\"");
        File.ReadAllLines(file).Length.Should().Be(3);
        using (new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))   // "Excel"
        {
            audit.Write("engineer", "Engineer", "logout", "P-1", "manual");
        }
        audit.Read(DateTime.Now.AddMinutes(-1), DateTime.Now.AddMinutes(1))
            .Select(e => e.Action).Should().Equal("apply", "online", "logout");
    }

    // ---------------- kết quả + ảnh ----------------

    [Fact]
    public async Task Recorder_Writes_One_Csv_Per_Command_With_Serial_Version_And_Ng_Images()
    {
        (CommandEngine engine, ProductStore store, ScriptedCamera camera, Func<double, double> setX) =
            Engine();
        FakeImageStore images = new();
        ResultRecorder recorder = new(engine, _paths.Results, _paths.Images, images,
            new ResultRecorderOptions(), NullLogger<ResultRecorder>.Instance);

        setX(10);
        CommandResult ok = await engine.ExecuteAsync("CH1", "T1", "SN-001");
        setX(20);
        await engine.ExecuteAsync("CH1", "T1", "SN,002");   // dấu phẩy trong serial
        recorder.Stop(TimeSpan.FromSeconds(5));

        ok.Serial.Should().Be("SN-001");
        ok.ProductVersion.Should().MatchRegex("^[0-9a-f]{12}$").And.Be(store.CurrentVersion);
        string[] lines = File.ReadAllLines(Directory.GetFiles(_paths.Results, "T1.csv",
            SearchOption.AllDirectories).Single(), Encoding.UTF8);
        lines[0].Should().Be("time,serial,status,error,Spot X,total_ms,channel,product,version,image");
        lines.Should().HaveCount(3);
        lines[2].Should().Contain("\"SN,002\",NG,").And.Contain(",20.0,");
        lines[2].Should().Contain(",NG,LIMIT:Spot X,");   // lý do NG không gửi máy nên ở file dữ liệu (DESIGN_LOG [29])
        images.Saved.Should().ContainSingle().Which.Should().EndWith("_NG.bmp");
        camera.Images.Should().OnlyContain(i => i.Disposed);   // ảnh đã ghi xong thì được trả
        recorder.Counters.Should().Be(new RecorderCounters(2, 1, 0, 0));

        IReadOnlyList<ResultRow> rows = new ResultReader(_paths.Results, _paths.Images)
            .Read(DateTime.Today);
        rows.Select(r => (r.Serial, r.Status)).Should().Equal(("SN-001", "OK"), ("SN,002", "NG"));
        rows[1].Values.Should().Equal(("Spot X", "20.0"));
        rows[1].ImageFile.Should().StartWith(_paths.Images).And.EndWith("_NG.bmp");
        rows[0].ImageFile.Should().BeEmpty();
        rows[1].Version.Should().Be(store.CurrentVersion);
    }

    [Fact]
    public async Task Image_Name_Follows_The_Pattern_And_An_Annotated_Copy_Is_Saved_Beside_It()
    {
        (CommandEngine engine, ProductStore store, _, Func<double, double> setX) = Engine();
        FakeImageStore images = new();
        ResultRecorder recorder = new(engine, _paths.Results, _paths.Images, images,
            new ResultRecorderOptions
            {
                SaveAnnotatedImages = true, ImageNamePattern = "{product}/{code}-{serial}-{status}",
            }, NullLogger<ResultRecorder>.Instance);
        setX(99);

        await engine.ExecuteAsync("CH1", "T1", "SN/7");   // "/" trong serial không được tạo thư mục
        recorder.Stop(TimeSpan.FromSeconds(5));

        string expected = Path.Combine(_paths.Images, store.Current!.Id, "T1-SN_7-NG");
        images.Saved.Should().ContainSingle().Which.Should().StartWith(expected);
        images.Annotated.Should().Equal(expected + "_view.jpg");
    }

    [Fact]
    public async Task Full_Image_Queue_Drops_The_Image_But_Keeps_The_Row()
    {
        (CommandEngine engine, _, ScriptedCamera camera, Func<double, double> setX) = Engine();
        FakeImageStore images = new();
        ResultRecorder recorder = new(engine, _paths.Results, _paths.Images, images,
            new ResultRecorderOptions { MaxPendingImages = 0 }, NullLogger<ResultRecorder>.Instance);
        setX(99);

        await engine.ExecuteAsync("CH1", "T1", "SN-9");
        recorder.Stop(TimeSpan.FromSeconds(5));

        recorder.Counters.Should().Be(new RecorderCounters(1, 0, 1, 0));
        images.Saved.Should().BeEmpty();
        camera.Images.Single().Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task Csv_Locked_Like_Excel_Keeps_Every_Row_In_The_Next_File()
    {
        (CommandEngine engine, _, _, Func<double, double> setX) = Engine();
        ResultRecorder recorder = new(engine, _paths.Results, _paths.Images, new FakeImageStore(),
            new ResultRecorderOptions(), NullLogger<ResultRecorder>.Instance);
        setX(5);
        await engine.ExecuteAsync("CH1", "T1", "SN-1");
        while (recorder.Counters.Rows < 1)
        {
            await Task.Delay(10);
        }
        string first = Directory.GetFiles(_paths.Results, "T1.csv", SearchOption.AllDirectories)
            .Single();

        // Excel mở file CSV thì giữ nó, cấm ghi, cho tới khi đóng.
        using (new FileStream(first, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await engine.ExecuteAsync("CH1", "T1", "SN-2");
            recorder.Stop(TimeSpan.FromSeconds(5));
            new ResultReader(_paths.Results, _paths.Images).Read(DateTime.Today)
                .Select(r => r.Serial).Should().Equal("SN-1", "SN-2");   // đọc được cả khi đang khoá
        }

        recorder.Counters.Failures.Should().Be(0);
        File.Exists(Path.Combine(Path.GetDirectoryName(first)!, "T1_2.csv")).Should().BeTrue();
    }

    // ---------------- dọn đĩa ----------------

    [Fact]
    public void Janitor_Deletes_By_Age_Then_By_Free_Space_But_Never_Today_Or_Foreign_Folders()
    {
        DateTime today = new(2026, 10, 28);
        foreach (int daysAgo in new[] { 40, 20, 10, 1, 0 })
        {
            string day = today.AddDays(-daysAgo).ToString("yyyy-MM-dd");
            Directory.CreateDirectory(Path.Combine(_paths.Images, day, "T1"));
            File.WriteAllText(Path.Combine(_paths.Images, day, "T1", "a.bmp"), "x");
            Directory.CreateDirectory(Path.Combine(_paths.Results, day));
        }
        Directory.CreateDirectory(Path.Combine(_paths.Images, "copied-by-someone"));
        double free = 10;   // mỗi thư mục xoá được "giải phóng" 8 GB
        StorageJanitor janitor = new(_paths.Images, _paths.Results,
            new RetentionOptions { KeepImageDays = 30, KeepResultDays = 15, ImageMinFreeGb = 25 },
            () => free, NullLogger<StorageJanitor>.Instance);
        int deletedImages = 0;

        CleanupReport report = janitor.Run(today);
        foreach (string f in report.DeletedFolders.Where(f => f.StartsWith("images/")))
        {
            deletedImages++;
        }

        report.DeletedFolders.Should().Contain(new[] { "images/2026-09-18", "results/2026-09-18",
            "results/2026-10-08" });
        Directory.Exists(Path.Combine(_paths.Images, "2026-10-28")).Should().BeTrue();
        Directory.Exists(Path.Combine(_paths.Images, "copied-by-someone")).Should().BeTrue();
        deletedImages.Should().Be(4);   // tuổi: 1; dung lượng (free không tăng): mọi ngày trừ hôm nay
    }

    // ---------------- phiên bản sản phẩm ----------------

    [Fact]
    public void Product_Version_Follows_Content_And_Switch_Reports_It()
    {
        WriteProduct(Product);
        string v1 = ProductBackup.ContentVersion(_paths.Product("P-1"));
        ProductBackup.ContentVersion(_paths.Product("P-1")).Should().Be(v1);
        WriteProduct(Product.Replace("\"upper\": 15", "\"upper\": 16"));
        string v2 = ProductBackup.ContentVersion(_paths.Product("P-1"));

        v2.Should().NotBe(v1).And.MatchRegex("^[0-9a-f]{12}$");
        ProductStore store = Store(new FakeJobInspector());
        ProductSwitchedEventArgs? seen = null;
        store.Switched += (_, e) => seen = e;
        store.Switch("P-1", Machine(), "channel CH1");
        seen!.Version.Should().Be(v2);
        seen.RequestedBy.Should().Be("channel CH1");
        store.CurrentVersion.Should().Be(v2);
        store.Close();
    }

    // ---------------- dựng ----------------

    // File do test tạo có chủ là người chạy test — giả như do quản trị Windows tạo (nhánh từ chối:
    // MaintenanceTests).
    private UserStore Users() => new(_paths.UsersFile, RecoveryFile(),
        _ => @"BUILTIN\Administrators");

    private string RecoveryFile() => Path.Combine(_paths.Root, UserStore.RecoveryFileName);

    private Session NewSession(AuditLog audit)
    {
        UserStore users = Users();
        users.Load();
        return new Session(users, audit, TimeSpan.FromMinutes(10), () => _now);
    }

    private (CommandEngine, ProductStore, ScriptedCamera, Func<double, double>) Engine()
    {
        WriteProduct(Product);
        double x = 0;
        FakeJobInspector inspector = new()
        {
            JobFactory = name => new FakeVisionJob(name)
            {
                Behavior = _ => new JobRun(JobRunStatus.Accept, string.Empty,
                    new Dictionary<string, object?> { ["X"] = x }, 1),
            },
        };
        ScriptedCamera camera = new("TopCam");
        DeviceManager devices = new(new DeviceManagerOptions(), NullLogger<DeviceManager>.Instance);
        devices.Add(camera);
        devices.OpenAllAsync().GetAwaiter().GetResult();
        ProductStore store = Store(inspector);
        store.Switch("P-1", Machine(), "test").Problems.Should().BeEmpty();
        CommandEngine engine = new(store, devices, NullLogger<CommandEngine>.Instance);
        return (engine, store, camera, v => x = v);
    }

    private ProductStore Store(FakeJobInspector inspector) =>
        new(_paths, new ProductLoader(inspector, new FakeCalibrationLoader()),
            new ProductBackup(_paths), NullLogger<ProductStore>.Instance);

    private static MachineView Machine() =>
        new(new[] { "TopCam" }, new Dictionary<string, (int, int)>(), new[] { "CH1" }, _ => true);

    private void WriteProduct(string json)
    {
        ProductPaths product = _paths.Product("P-1");
        Directory.CreateDirectory(product.Jobs);
        File.WriteAllText(product.ProductFile, json, Encoding.UTF8);
        File.WriteAllText(product.Job("top.vpp"), "vpp");
    }

    /// <summary>Kho ảnh giả: ghi một file nhỏ để kiểm đường dẫn, không cần VisionPro.</summary>
    private sealed class FakeImageStore : IImageStore
    {
        public List<string> Saved { get; } = new();

        public string Extension => ".bmp";

        public void Save(CameraImage image, string file)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, image.Source);
            Saved.Add(file);
        }

        public CameraImage Load(string file) => throw new NotSupportedException();

        public List<string> Annotated { get; } = new();

        public bool SaveAnnotated(object record, string preferredKey, string file)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, "view");
            Annotated.Add(file);
            return true;
        }
    }
}
