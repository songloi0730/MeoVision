using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FluentAssertions;
using MeoVision.App.Hosting;
using MeoVision.App.Products;
using MeoVision.Core.Commands;
using MeoVision.Core.Localization;
using MeoVision.Core.Products;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MeoVision.App.Tests;

/// <summary>
/// Bộ kiểm job giả: trả terminal theo tên file, đếm số lần mở — để test bộ nạp không cần VisionPro
/// (bộ kiểm thật có test riêng trong MeoVision.Vision.Tests). Mỗi lần mở tạo một job giả để chạy
/// (Chương 20); <see cref="Created"/> giữ chúng để test kiểm đã đóng hay chưa.
/// </summary>
internal sealed class FakeJobInspector : IJobInspector
{
    public Dictionary<string, JobInspection> Jobs { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, int> Opened { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<FakeVisionJob> Created { get; } = new();

    public Func<string, FakeVisionJob> JobFactory { get; set; } = name => new FakeVisionJob(name);

    public JobInspection Inspect(string file)
    {
        string name = Path.GetFileName(file);
        Opened[name] = Opened.TryGetValue(name, out int n) ? n + 1 : 1;
        JobInspection inspection = Jobs.TryGetValue(name, out JobInspection? job)
            ? job
            : Job(outputs: new[] { ("X", typeof(double)), ("Y", typeof(double)) });
        FakeVisionJob created = JobFactory(name);
        Created.Add(created);
        return inspection with { Job = created };
    }

    public static JobInspection Job(IEnumerable<(string, Type)> outputs, bool inputImage = true,
        Msg? scriptError = null) =>
        new(null,
            inputImage ? new[] { new TerminalInfo("InputImage", typeof(object)) }
                       : Array.Empty<TerminalInfo>(),
            outputs.Select(o => new TerminalInfo(o.Item1, o.Item2)).ToList(), scriptError, 5);
}

public sealed class ProductTests : IDisposable
{
    private const string ValidProduct = @"{
  ""schemaVersion"": 1, ""id"": ""P-1024"", ""name"": ""Aluminium cover A"",
  ""commands"": [
    { ""code"": ""T1"", ""name"": ""Top alignment"", ""channel"": ""CH1"", ""camera"": ""TopCam"",
      ""job"": ""T1_align.vpp"", ""exposureMs"": 1.5,
      ""lights"": [ { ""role"": ""MainLight"", ""channel"": 1, ""intensity"": 180 } ],
      ""outputs"": [ { ""terminal"": ""X"", ""unit"": ""mm"", ""offset"": 0.02 },
                     { ""terminal"": ""Y"", ""unit"": ""mm"" } ] },
    { ""code"": ""T3"", ""name"": ""Bottom defects"", ""channel"": ""CH2"",
      ""camera"": ""BottomCam"", ""job"": ""T3_defect.vpp"", ""calibration"": ""Calib-1"",
      ""outputs"": [ { ""terminal"": ""X"", ""lower"": 0, ""upper"": 0.5, ""format"": ""0.0"" } ] }
  ]
}";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "mv-products-" +
        Guid.NewGuid().ToString("N"));
    private readonly AppPaths _paths;
    private readonly FakeJobInspector _inspector = new();

    public ProductTests()
    {
        _paths = AppPaths.Resolve(_root, Path.GetTempPath());
        _paths.EnsureCreated();
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private static MachineView Machine() => new(
        new[] { "TopCam", "BottomCam" },
        new Dictionary<string, (int, int)> { ["MainLight"] = (4, 255) },
        new[] { "CH1", "CH2" },
        id => id == "Calib-1");

    private ProductPaths Write(string id, string json, params string[] jobs)
    {
        ProductPaths product = _paths.Product(id);
        Directory.CreateDirectory(product.Jobs);
        File.WriteAllText(product.ProductFile, json, Encoding.UTF8);
        foreach (string job in jobs)
        {
            File.WriteAllText(product.Job(job), "vpp " + job);
        }
        return product;
    }

    private readonly FakeCalibrationLoader _calibrations = new();

    private ProductLoadResult Load(string id = "P-1024") =>
        new ProductLoader(_inspector, _calibrations).Load(_paths.Product(id), id, Machine());

    [Fact]
    public void Valid_Product_Loads_And_Warns_About_Unused_Job_Files()
    {
        Write("P-1024", ValidProduct, "T1_align.vpp", "T3_defect.vpp", "T3_defect - Copy.vpp");

        ProductLoadResult result = Load();

        result.Problems.Should().BeEmpty();
        result.IsValid.Should().BeTrue();
        result.Product!.Commands.Select(c => c.Code).Should().Equal("T1", "T3");
        result.Warnings.Single().Key.Should().Be("product.unusedJobs");
        result.Warnings.Single().Args[1].Should().Be("T3_defect - Copy.vpp");
    }

    [Fact]
    public void Every_Problem_Is_Reported_In_One_Pass()
    {
        Write("P-1024", @"{
  ""schemaVersion"": 1, ""id"": ""P-9999"", ""name"": """",
  ""commands"": [
    { ""code"": ""t1"", ""channel"": ""CH9"", ""camera"": ""SideCam"", ""job"": ""C:\\jobs\\a.vpp"" },
    { ""code"": ""T2"", ""channel"": ""CH1"", ""camera"": ""TopCam"", ""job"": ""missing.vpp"",
      ""calibration"": ""Calib-7"", ""timeoutMs"": 0, ""retry"": 9,
      ""lights"": [ { ""role"": ""MainLight"", ""channel"": 5, ""intensity"": 300 },
                    { ""role"": ""RingLight"", ""channel"": 1, ""intensity"": 10 } ] },
    { ""code"": ""T2"", ""channel"": ""CH1"", ""camera"": ""TopCam"", ""job"": ""T1_align.vpp"",
      ""outputs"": [ { ""terminal"": ""Z"" }, { ""terminal"": ""X"", ""lower"": 5, ""upper"": 1,
                       ""format"": ""abc"" } ] },
    { ""code"": ""SW"", ""channel"": ""CH1"", ""camera"": ""TopCam"", ""job"": ""T1_align.vpp"" }
  ]
}", "T1_align.vpp");

        ProductLoadResult result = Load();

        result.IsValid.Should().BeFalse();
        result.Problems.Select(p => p.Key).Should().BeEquivalentTo(new[]
        {
            "product.idMismatch", "product.nameEmpty",
            "product.cmd.badCode", "product.cmd.noChannel", "product.cmd.noCamera",
            "product.cmd.badJobName",
            "product.cmd.noCalibration", "product.cmd.badTimeout", "product.cmd.badRetry",
            "product.cmd.badLightChannel", "product.cmd.badIntensity", "product.cmd.noLight",
            "product.cmd.jobMissing",
            "product.cmd.duplicate", "product.out.noTerminal", "product.out.limits",
            "product.out.badFormat",
            "product.cmd.reservedCode",
        });
    }

    [Fact]
    public void Misspelled_Field_Is_An_Error_Not_Silently_Ignored()
    {
        Write("P-1024", ValidProduct.Replace("\"commands\"", "\"comands\""));

        ProductLoadResult result = Load();

        result.Problems.Single().Key.Should().Be("product.invalidJson");
        result.Product.Should().BeNull();
    }

    [Fact]
    public void Job_Contract_Is_Checked_Against_The_Real_Terminals()
    {
        Write("P-1024", ValidProduct, "T1_align.vpp", "T3_defect.vpp");
        _inspector.Jobs["T1_align.vpp"] = FakeJobInspector.Job(
            new[] { ("X", typeof(double)), ("Y", typeof(string)) }, inputImage: false);
        _inspector.Jobs["T3_defect.vpp"] = FakeJobInspector.Job(
            new[] { ("X", typeof(string)) }, scriptError: Msg.Of("job.scriptCompile", "CS1056"));

        ProductLoadResult result = Load();

        result.Problems.Select(p => p.Key).Should().BeEquivalentTo(new[]
        {
            "product.cmd.noInputImage",   // T1: thiếu terminal InputImage
            "product.cmd.jobScript",      // T3: script không biên dịch
            "product.out.notNumber",      // T3: X là chuỗi nhưng có lower/upper
        });
    }

    [Fact]
    public void A_Job_Used_By_Several_Commands_Is_Opened_Once()
    {
        Write("P-1024", ValidProduct.Replace("T3_defect.vpp", "T1_align.vpp"), "T1_align.vpp");

        Load().IsValid.Should().BeTrue();

        _inspector.Opened["T1_align.vpp"].Should().Be(1);
    }

    [Fact]
    public void Checked_Jobs_Are_Kept_Open_When_Valid_And_Closed_When_Not()
    {
        Write("P-1024", ValidProduct, "T1_align.vpp", "T3_defect.vpp");
        Write("P-2048", ValidProduct.Replace("P-1024", "P-2048").Replace("CH2", "CH9"),
            "T1_align.vpp", "T3_defect.vpp");

        ProductLoadResult valid = Load();
        ProductLoadResult invalid = Load("P-2048");

        // Hợp lệ: chính đối tượng đã kiểm được giữ để chạy — không mở file lần hai.
        valid.Loaded!.Job("T1_align.vpp").Should().BeSameAs(_inspector.Created[0]);
        valid.Loaded.Calibration("Calib-1").Should().NotBeNull();
        _inspector.Created.Take(2).Should().OnlyContain(j => !j.Disposed);
        // Không hợp lệ: không giữ gì, job đã mở phải đóng ngay.
        invalid.Loaded.Should().BeNull();
        _inspector.Created.Skip(2).Should().OnlyContain(j => j.Disposed);
        _calibrations.Created.Last().Disposed.Should().BeTrue();
    }

    [Fact]
    public void Calibration_That_Cannot_Be_Used_Is_A_Problem()
    {
        Write("P-1024", ValidProduct, "T1_align.vpp", "T3_defect.vpp");
        _calibrations.Errors["Calib-1"] = Msg.Of("calibration.notCalibrated", "Calib-1", "Tool");

        ProductLoadResult result = Load();

        result.Problems.Single().Key.Should().Be("product.cmd.calibrationUnusable");
    }

    [Fact]
    public void Switch_Refuses_An_Invalid_Product_And_Keeps_The_Current_One()
    {
        Write("P-1024", ValidProduct, "T1_align.vpp", "T3_defect.vpp");
        Write("P-2048", ValidProduct.Replace("P-1024", "P-2048"));   // thiếu cả hai file job
        ProductStore store = NewStore();
        store.Switch("P-1024", Machine(), "engineer").IsValid.Should().BeTrue();

        ProductLoadResult refused = store.Switch("P-2048", Machine(), "machine SW");

        refused.IsValid.Should().BeFalse();
        store.Current!.Id.Should().Be("P-1024");
        store.ActiveId.Should().Be("P-1024");
    }

    [Fact]
    public void Backup_Stores_Each_Content_Once_And_Restores_Exactly()
    {
        ProductPaths product = Write("P-1024", ValidProduct, "T1_align.vpp", "T3_defect.vpp");
        ProductBackup backup = new(_paths);
        ProductStore store = NewStore(backup);
        ProductConfig config = Load().Product!;

        config.Commands[0].ExposureMs = 2.0;
        ProductSnapshot first = store.Save(config, "engineer", "exposure 1.5 -> 2.0");
        config.Commands[0].ExposureMs = 2.5;
        store.Save(config, "engineer", "exposure 2.0 -> 2.5");

        // Mỗi lần lưu chụp trạng thái TRƯỚC khi ghi: product.json gốc + bản 2.0, hai job chỉ lưu
        // một lần vì không đổi → 4 nội dung.
        string blobs = Path.Combine(_paths.Backup, "products", "P-1024", "blobs");
        Directory.GetFiles(blobs).Should().HaveCount(4);
        first.Files.Select(f => f.Path).Should().Equal(
            "product.json", "jobs/T1_align.vpp", "jobs/T3_defect.vpp");

        File.WriteAllText(product.Job("T9_new.vpp"), "added later");
        RestoreResult restored = backup.Restore(product, "P-1024", first.Name, "engineer");

        restored.Ok.Should().BeTrue();
        File.ReadAllText(product.ProductFile).Should().Be(ValidProduct);
        File.Exists(product.Job("T9_new.vpp")).Should().BeFalse();   // không có trong bản sao lưu
        backup.List("P-1024").First().Reason.Should().StartWith("before-restore");
    }

    [Fact]
    public void Corrupt_Backup_Is_Detected_And_Nothing_Is_Touched()
    {
        ProductPaths product = Write("P-1024", ValidProduct, "T1_align.vpp", "T3_defect.vpp");
        ProductBackup backup = new(_paths);
        ProductSnapshot snapshot = backup.Create(product, "P-1024", "test", "engineer");
        string blob = Path.Combine(_paths.Backup, "products", "P-1024", "blobs",
            snapshot.Files[1].Sha256);
        File.WriteAllText(blob, "bit rot");
        File.WriteAllText(product.ProductFile, "{ edited }");

        RestoreResult result = backup.Restore(product, "P-1024", snapshot.Name, "engineer");

        result.Ok.Should().BeFalse();
        result.Message.Key.Should().Be("backup.corrupt");
        File.ReadAllText(product.ProductFile).Should().Be("{ edited }");
    }

    [Fact]
    public void Prune_Keeps_The_Newest_And_Removes_Unreferenced_Content()
    {
        ProductPaths product = Write("P-1024", ValidProduct, "T1_align.vpp");
        ProductBackup backup = new(_paths);
        for (int i = 0; i < 4; i++)
        {
            File.WriteAllText(product.ProductFile, ValidProduct + i);
            backup.Create(product, "P-1024", "edit " + i, "engineer");
            System.Threading.Thread.Sleep(5);   // tên bản sao lưu theo mili giây
        }

        backup.Prune("P-1024", keep: 2).Should().Be(2);

        backup.List("P-1024").Select(s => s.Reason).Should().Equal("edit 3", "edit 2");
        // còn lại: 2 nội dung product.json + 1 job
        Directory.GetFiles(Path.Combine(_paths.Backup, "products", "P-1024", "blobs"))
            .Should().HaveCount(3);
    }

    [Fact]
    public void Extra_Cameras_Brightness_Settle_And_Slot_Are_Checked_Before_Switching()
    {
        Write("P-1024", ValidProduct.Replace(@"""exposureMs"": 1.5,", @"""exposureMs"": 1.5,
      ""brightness"": 1.5, ""display"": { ""slot"": -1 },
      ""extraCameras"": [ { ""camera"": ""SideCam"", ""terminal"": ""SideImage"" },
                         { ""camera"": ""TopCam"", ""terminal"": ""InputImage"" },
                         { ""camera"": ""BottomCam"", ""terminal"": ""Count"" } ],")
            .Replace(@"""intensity"": 180 }", @"""intensity"": 180, ""settleMs"": 5000 }"),
            "T1_align.vpp", "T3_defect.vpp");
        _inspector.Jobs["T1_align.vpp"] = TwoImageJob();

        ProductLoadResult result = Load();

        result.Problems.Select(p => p.Key).Should().BeEquivalentTo(new[]
        {
            "product.cmd.noCamera", "product.cmd.cameraTwice", "product.cmd.extraTerminal",
            "product.cmd.extraTerminal", "product.cmd.badCameraParam", "product.cmd.badSettle",
            "product.cmd.badSlot",
        });
    }

    [Fact]
    public void Valid_Two_Camera_Command_With_Shared_Frame_Loads()
    {
        Write("P-1024", ValidProduct.Replace(@"""exposureMs"": 1.5,", @"""exposureMs"": 1.5,
      ""brightness"": 0.4, ""contrast"": 0.6, ""display"": { ""slot"": 1 },
      ""extraCameras"": [ { ""camera"": ""BottomCam"", ""terminal"": ""SideImage"",
                           ""calibration"": ""Calib-1"", ""exposureMs"": 3 } ],")
            .Replace(@"""intensity"": 180 }", @"""intensity"": 180, ""settleMs"": 150 }"),
            "T1_align.vpp", "T3_defect.vpp");
        _inspector.Jobs["T1_align.vpp"] = TwoImageJob();

        ProductLoadResult result = Load();

        result.Problems.Should().BeEmpty();
        CommandConfig t1 = result.Product!.Commands[0];
        t1.ExtraCameras.Single().Camera.Should().Be("BottomCam");
        t1.Lights.Single().SettleMs.Should().Be(150);
        t1.Display.Slot.Should().Be(1);
    }

    [Fact]
    public void Command_Kind_Must_Be_Inspect_Or_Guide()
    {
        Write("P-1024", ValidProduct.Replace("\"code\": \"T3\",", "\"code\": \"T3\", \"kind\": \"Aim\","),
            "T1_align.vpp", "T3_defect.vpp");

        Load().Problems.Select(p => p.Key).Should().Equal("product.cmd.badKind");

        Write("P-1024", ValidProduct.Replace("\"code\": \"T3\",", "\"code\": \"T3\", \"kind\": \"guide\","),
            "T1_align.vpp", "T3_defect.vpp");
        ProductLoadResult ok = Load();
        ok.Problems.Should().BeEmpty();
        CommandKind.IsGuide(ok.Product!.Commands[1].Kind).Should().BeTrue();
        CommandKind.IsGuide(ok.Product.Commands[0].Kind).Should().BeFalse();   // mặc định Inspect
    }

    private static JobInspection TwoImageJob() =>
        new(null,
            new[]
            {
                new TerminalInfo("InputImage", typeof(object)),
                new TerminalInfo("SideImage", typeof(object)),
                new TerminalInfo("Count", typeof(int)),
            },
            new[] { new TerminalInfo("X", typeof(double)), new TerminalInfo("Y", typeof(double)) },
            null, 5);

    private ProductStore NewStore(ProductBackup? backup = null) =>
        new(_paths, new ProductLoader(_inspector, _calibrations), backup ?? new ProductBackup(_paths),
            NullLogger<ProductStore>.Instance);
}
