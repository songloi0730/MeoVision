using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MeoVision.App.Commands;
using MeoVision.App.Devices;
using MeoVision.App.Hosting;
using MeoVision.App.Products;
using MeoVision.Core.Commands;
using MeoVision.Core.Devices;
using MeoVision.Core.Localization;
using MeoVision.Core.Products;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MeoVision.App.Tests;

/// <summary>
/// Chương 22: tham số nhanh, khung ảnh dùng chung, vòng ảnh gần nhất, bản nháp, chạy kèm, áp dụng
/// và quay lại — với job giả (job nào cũng trả X = giá trị tham số "MinArea" nó nhận được).
/// </summary>
public sealed class DraftTests : IDisposable
{
    private const string Product = @"{
  ""schemaVersion"": 1, ""id"": ""P-1"", ""name"": ""Draft test"",
  ""commands"": [
    { ""code"": ""T1"", ""name"": ""Top"", ""channel"": ""CH1"", ""camera"": ""TopCam"",
      ""job"": ""top.vpp"", ""timeoutMs"": 1000,
      ""outputs"": [ { ""terminal"": ""X"", ""name"": ""X"", ""format"": ""0.0"",
                       ""lower"": 0, ""upper"": 15 } ],
      ""tunables"": [ { ""terminal"": ""MinArea"", ""name"": ""Min area"", ""unit"": ""px"",
                        ""min"": 1, ""max"": 50, ""step"": 1, ""value"": 10 } ] }
  ]
}";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "mv-draft-" +
        Guid.NewGuid().ToString("N"));
    private readonly AppPaths _paths;
    private readonly FakeJobInspector _inspector = new();
    private readonly ScriptedCamera _top = new("TopCam");
    private readonly DeviceManager _devices;
    private readonly ProductStore _store;
    private readonly CommandEngine _engine;
    private readonly MachineView _machine = Machine("TopCam");

    public DraftTests()
    {
        _paths = AppPaths.Resolve(_root, Path.GetTempPath());
        _paths.EnsureCreated();
        _inspector.Jobs["top.vpp"] = new JobInspection(null,
            new[]
            {
                new TerminalInfo("InputImage", typeof(object)),
                new TerminalInfo("MinArea", typeof(double)),
                new TerminalInfo("Mode", typeof(string)),
            },
            new[] { new TerminalInfo("X", typeof(double)) }, null, 1);
        _inspector.JobFactory = name =>
        {
            FakeVisionJob job = new(name);
            job.Behavior = _ => new JobRun(JobRunStatus.Accept, string.Empty,
                new Dictionary<string, object?> { ["X"] = job.LastRequest!.Inputs["MinArea"] }, 1);
            return job;
        };
        WriteProduct(Product);
        _devices = new DeviceManager(new DeviceManagerOptions(),
            NullLogger<DeviceManager>.Instance);
        _devices.Add(_top);
        _devices.OpenAllAsync().GetAwaiter().GetResult();
        _store = new ProductStore(_paths,
            new ProductLoader(_inspector, new FakeCalibrationLoader()), new ProductBackup(_paths),
            NullLogger<ProductStore>.Instance);
        _store.Switch("P-1", _machine, "test").Problems.Should().BeEmpty();
        _engine = new CommandEngine(_store, _devices, NullLogger<CommandEngine>.Instance);
    }

    public void Dispose()
    {
        _engine.Dispose();
        _devices.Dispose();
        _store.Close();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Theory]
    [InlineData(@"""terminal"": ""MinArea""", @"""terminal"": ""Missing""",
        "product.tune.noTerminal")]
    [InlineData(@"""terminal"": ""MinArea""", @"""terminal"": ""Mode""",
        "product.tune.notNumber")]
    [InlineData(@"""terminal"": ""MinArea""", @"""terminal"": ""InputImage""",
        "product.tune.badTerminal")]
    [InlineData(@"""value"": 10", @"""value"": 99", "product.tune.outOfRange")]
    [InlineData(@"""step"": 1", @"""step"": 0", "product.tune.range")]
    public void Loader_Checks_Tunables_Against_The_Real_Input_Terminals(string find,
        string replace, string expected)
    {
        WriteProduct(Product.Replace(find, replace));

        ProductLoadResult result = new ProductLoader(_inspector, new FakeCalibrationLoader())
            .Load(_paths.Product("P-1"), "P-1", _machine);

        result.Problems.Select(p => p.Key).Should().Equal(expected);
    }

    [Fact]
    public async Task Engine_Puts_Tunables_Into_The_Job_And_Releases_The_Image_After_Completed()
    {
        FakeVisionJob job = _inspector.Created.Single();

        CommandResult result = await _engine.ExecuteAsync("CH1", "T1");

        job.LastRequest!.Inputs["MinArea"].Should().Be(10);
        job.LastRequest.WithRecord.Should().BeTrue();
        result.Outputs.Single().Text.Should().Be("10.0");
        result.Frame!.Record.Should().BeSameAs(job.Record);
        _top.Images.Single().Disposed.Should().BeTrue();   // không ai Share → trả ngay
    }

    [Fact]
    public void Display_Frame_Frees_The_Image_Only_When_The_Last_Holder_Disposes()
    {
        ScriptedImage image = new();
        DisplayFrame first = new(image, "running");
        DisplayFrame second = first.ShareWith("draft");

        first.Dispose();
        first.Dispose();   // trả hai lần không được tính hai lần
        image.Disposed.Should().BeFalse();
        second.Record.Should().Be("draft");
        second.Dispose();

        image.Disposed.Should().BeTrue();
        first.Invoking(f => f.Share()).Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public async Task Recent_Frames_Keep_The_Last_N_Images_Per_Command()
    {
        using RecentFrames recent = new(_engine, perCommand: 2);
        for (int i = 0; i < 3; i++)
        {
            await _engine.ExecuteAsync("CH1", "T1");
        }

        _top.Images.Select(i => i.Disposed).Should().Equal(true, false, false);
        IReadOnlyList<CommandResult> taken = recent.Take("T1");
        taken.Select(r => r.Frame!.Image).Should().Equal(_top.Images[2], _top.Images[1]);

        recent.Dispose();
        _top.Images[2].Disposed.Should().BeFalse();   // bản lấy ra vẫn giữ ảnh
        foreach (CommandResult r in taken)
        {
            r.Frame!.Dispose();
        }
        _top.Images.Should().OnlyContain(i => i.Disposed);
    }

    [Fact]
    public void Old_Draft_That_Cannot_Be_Deleted_Does_Not_Stop_A_New_Draft()
    {
        // Lỗi thật 2026-10-30: OneDrive giữ thư mục bản nháp cũ, Thiết lập lệnh không mở được.
        string old = Path.Combine(_paths.Drafts, "P-1");
        Directory.CreateDirectory(old);
        using (new FileStream(Path.Combine(old, "locked.bin"), FileMode.Create, FileAccess.Write,
                   FileShare.None))
        {
            using DraftSession draft = Open();

            draft.Job("T1").Should().NotBeNull();
            Directory.GetDirectories(_paths.Drafts, "P-1-*").Should().ContainSingle()
                .Which.Should().Match(d => File.Exists(Path.Combine(d, "jobs", "top.vpp")));
        }
    }

    [Fact]
    public void Draft_Has_Its_Own_Jobs_And_Does_Not_Touch_The_Running_Product()
    {
        FakeVisionJob running = _inspector.Created.Single();

        using DraftSession draft = Open();
        draft.SetTunable("T1", "MinArea", 20);
        draft.SetLimits("T1", "X", 0, 25, 0);

        draft.Job("T1").Should().NotBeSameAs(running);
        _store.Current!.Commands[0].Tunables[0].Value.Should().Be(10);
        draft.Changes(_store.Current).Select(m => m.Key).Should()
            .Equal("draft.change.tunable", "draft.change.limits");
        draft.Invoking(d => d.SetTunable("T1", "MinArea", 51)).Should()
            .Throw<ArgumentOutOfRangeException>();
        File.Exists(Path.Combine(_paths.Drafts, "P-1", "jobs", "top.vpp")).Should().BeTrue();
    }

    [Fact]
    public async Task Shadow_Runs_The_Draft_On_The_Same_Image_And_Counts_Differences()
    {
        using DraftSession draft = Open();
        using ShadowRunner shadow = new(_engine, NullLogger<ShadowRunner>.Instance);
        using SemaphoreSlim compared = new(0);
        shadow.Compared += (_, _) => compared.Release();
        shadow.Attach(draft);

        await _engine.ExecuteAsync("CH1", "T1");                   // lệnh chưa sửa: không chạy kèm
        (await compared.WaitAsync(300)).Should().BeFalse();
        draft.MarkJobEdited("T1");                                 // sửa job, kết quả vẫn thế
        await _engine.ExecuteAsync("CH1", "T1");
        (await compared.WaitAsync(2000)).Should().BeTrue();
        draft.SetTunable("T1", "MinArea", 20);                     // X = 20 → NG ở bản nháp
        CommandResult result = await _engine.ExecuteAsync("CH1", "T1");
        (await compared.WaitAsync(2000)).Should().BeTrue();

        result.Status.Should().Be(CommandStatus.Ok);              // máy vẫn nhận kết quả bản chạy
        shadow.Counters.Should().Be(new ShadowCounters(1, 1, 0));
        ShadowDifference diff = shadow.Differences.Single();
        diff.Draft.Status.Should().Be(CommandStatus.Ng);
        diff.Draft.Frame!.Image.Should().BeSameAs(diff.Running.Frame!.Image);
        _top.Images.Select(i => i.Disposed).Should().Equal(true, true, false);   // giữ ảnh khác

        draft.EditorOpen = true;
        await _engine.ExecuteAsync("CH1", "T1");
        shadow.Counters.Skipped.Should().Be(1);

        shadow.Attach(null);
        _top.Images.Should().OnlyContain(i => i.Disposed);
    }

    [Fact]
    public async Task Apply_Switches_Between_Commands_And_Rollback_Restores_Files_And_Product()
    {
        string originalJson = File.ReadAllText(_paths.Product("P-1").ProductFile);
        using DraftSession draft = Open();
        draft.SetTunable("T1", "MinArea", 12);
        draft.MarkJobEdited("T1");

        ApplyResult applied = _store.Apply(draft, "raise min area", _machine);

        applied.Ok.Should().BeTrue();
        _store.Current!.Commands[0].Tunables[0].Value.Should().Be(12);
        byte[] written = File.ReadAllBytes(_paths.Product("P-1").ProductFile);
        written[0].Should().Be((byte)'{');                       // không BOM
        string json = Encoding.UTF8.GetString(written);
        json.Should().Contain("\"value\": 12.0").And.NotContain("\"Value\"").And
            .NotContain("null");                                 // kiểu người viết tay
        File.ReadAllText(_paths.Product("P-1").Job("top.vpp")).Should().Be("top.vpp");  // Save
        (await _engine.ExecuteAsync("CH1", "T1")).Outputs.Single().Text.Should().Be("12.0");

        ApplyResult back = _store.RollBack("P-1", applied.PreviousSnapshot!, _machine, "test");

        back.Ok.Should().BeTrue();
        _store.Current!.Commands[0].Tunables[0].Value.Should().Be(10);
        File.ReadAllText(_paths.Product("P-1").ProductFile).Should().Be(originalJson);
        File.ReadAllText(_paths.Product("P-1").Job("top.vpp")).Should().Be("vpp");
        (await _engine.ExecuteAsync("CH1", "T1")).Outputs.Single().Text.Should().Be("10.0");
    }

    [Fact]
    public void Apply_That_Fails_The_Check_Leaves_Files_And_Running_Product_Unchanged()
    {
        string originalJson = File.ReadAllText(_paths.Product("P-1").ProductFile);
        ProductConfig before = _store.Current!;
        using DraftSession draft = Open();
        draft.SetTunable("T1", "MinArea", 12);

        // Camera bị gỡ khỏi máy trong lúc sửa: bản mới không qua kiểm tra.
        ApplyResult applied = _store.Apply(draft, "x", Machine("OtherCam"));

        applied.Ok.Should().BeFalse();
        applied.Problems.Select(p => p.Key).Should().Contain("product.cmd.noCamera");
        _store.Current.Should().BeSameAs(before);
        File.ReadAllText(_paths.Product("P-1").ProductFile).Should().Be(originalJson);
    }

    [Fact]
    public void Apply_Is_Refused_When_Another_Product_Was_Switched_In_Meanwhile()
    {
        using DraftSession draft = Open();
        Directory.CreateDirectory(_paths.Product("P-2").Jobs);
        File.WriteAllText(_paths.Product("P-2").ProductFile, Product.Replace("P-1", "P-2"));
        File.WriteAllText(_paths.Product("P-2").Job("top.vpp"), "vpp");
        _store.Switch("P-2", _machine, "test").Problems.Should().BeEmpty();

        ApplyResult applied = _store.Apply(draft, "x", _machine);

        applied.Problems.Single().Key.Should().Be("draft.productChanged");
        _store.Current!.Id.Should().Be("P-2");
    }

    [Fact]
    public void Replay_Runs_Saved_Images_Of_The_Running_Version_In_Capture_Order()
    {
        using DraftSession draft = Open();
        FakeVisionJob job = _inspector.Created.Last();   // job NHÁP — đối tượng riêng
        List<string> order = new();
        job.Behavior = image =>
        {
            order.Add(Path.GetFileName(image.Source));
            return new JobRun(JobRunStatus.Accept, string.Empty,
                new Dictionary<string, object?> { ["X"] = job.LastRequest!.Inputs["MinArea"] }, 1);
        };
        string version = _store.CurrentVersion;
        string day = DateTime.Now.ToString("yyyy-MM-dd");
        string images = Path.Combine(_paths.Images, day, "T1");
        Directory.CreateDirectory(images);
        foreach (string name in new[] { "a1.bmp", "a2.bmp", "a3.bmp", "a4.bmp" })
        {
            File.WriteAllText(Path.Combine(images, name), "img");
        }
        string Row(string time, string serial, string status, string x, string v, string image) =>
            $"{day} {time},{serial},{status},,{x},3,CH1,P-1,{v}," +
            (image.Length == 0 ? "" : $"{day}/T1/{image}");
        Directory.CreateDirectory(Path.Combine(_paths.Results, day));
        File.WriteAllLines(Path.Combine(_paths.Results, day, "T1.csv"), new[]
        {
            "time,serial,status,error,X,total_ms,channel,product,version,image",
            Row("10:00:03.000", "SN3", "OK", "10.0", version, "a3.bmp"),
            Row("10:00:01.000", "SN1", "OK", "10.0", version, "a1.bmp"),   // ghi trễ hơn
            Row("10:00:02.000", "SN2", "NG", "12.0", version, "a2.bmp"),   // máy đã trả khác
            Row("10:00:04.000", "SN4", "OK", "10.0", "0123456789ab", "a4.bmp"),   // bản cũ
            Row("10:00:05.000", "SN5", "OK", "10.0", version, "gone.bmp"),   // đã bị dọn
            Row("10:00:06.000", "SN6", "OK", "10.0", version, ""),           // không lưu ảnh
        });
        ReplayRunner replay = new(new Results.ResultReader(_paths.Results, _paths.Images),
            new NamedImageStore());

        ReplayReport report = replay.Run(draft, "T1", version, DateTime.Now.AddDays(-1),
            DateTime.Now, maxImages: 100);

        order.Should().Equal("a1.bmp", "a2.bmp", "a3.bmp");   // theo thời gian, không theo file
        report.Images.Should().Be(4);
        report.Same.Should().Be(2);
        report.Differences.Single().Recorded.Serial.Should().Be("SN2");
        report.OtherVersion.Should().Be(1);
        report.MissingImages.Should().Be(1);
    }

    /// <summary>Kho ảnh giả: ảnh mang tên file để kiểm thứ tự chạy.</summary>
    private sealed class NamedImageStore : Core.Storage.IImageStore
    {
        public string Extension => ".bmp";

        public void Save(CameraImage image, string file) => throw new NotSupportedException();

        public CameraImage Load(string file) => new NamedImage(file);

        public bool SaveAnnotated(object record, string preferredKey, string file) => false;

        private sealed class NamedImage : CameraImage
        {
            public NamedImage(string file)
                : base(file, 64, 48, DateTime.Now)
            {
            }

            protected override void Dispose(bool disposing)
            {
            }
        }
    }

    private DraftSession Open()
    {
        DraftOpenResult opened = _store.OpenDraft(_machine, "engineer");
        opened.Problems.Should().BeEmpty();
        return opened.Draft!;
    }

    private void WriteProduct(string json)
    {
        ProductPaths product = _paths.Product("P-1");
        Directory.CreateDirectory(product.Jobs);
        File.WriteAllText(product.ProductFile, json, Encoding.UTF8);
        File.WriteAllText(product.Job("top.vpp"), "vpp");
    }

    private static MachineView Machine(string camera) =>
        new(new[] { camera }, new Dictionary<string, (int, int)>(), new[] { "CH1" }, _ => true);
}
