using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FluentAssertions;
using MeoVision.App.Commands;
using MeoVision.App.Hosting;
using MeoVision.App.Products;
using MeoVision.App.Results;
using MeoVision.Core.Products;
using Xunit;

namespace MeoVision.App.Tests;

/// <summary>
/// Phần cấu hình học từ hai chương trình tham khảo (DESIGN_LOG [26]): xếp khung, bộ đếm giữ qua
/// lần khởi động, danh sách sản phẩm cho form Sản phẩm, thư mục dữ liệu cạnh exe và ảnh ở ổ khác.
/// </summary>
public sealed class LayoutAndStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mv-layout-" +
        Guid.NewGuid().ToString("N"));

    public LayoutAndStorageTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Theory]
    [InlineData(1, 0, 1, 1)]
    [InlineData(2, 0, 2, 1)]
    [InlineData(3, 0, 3, 1)]   // lỗi cũ: 3 lệnh thành lưới 2×2 với một ô trống
    [InlineData(4, 0, 2, 2)]
    [InlineData(5, 0, 3, 2)]
    [InlineData(7, 0, 3, 3)]
    [InlineData(3, 2, 2, 2)]   // cấu hình cứng 2 cột
    [InlineData(2, 5, 2, 1)]   // cấu hình nhiều cột hơn số khung
    [InlineData(0, 0, 1, 1)]
    public void Grid_Has_No_Empty_Row(int frames, int configured, int columns, int rows)
    {
        FrameLayout.Grid(frames, configured).Should().Be((columns, rows));
    }

    [Fact]
    public void Commands_With_The_Same_Slot_Share_One_Frame_In_Order_Of_First_Use()
    {
        CommandConfig[] commands =
        {
            Command("T1", 0), Command("T2", 1), Command("T3", 1), Command("T4", 0), Command("T5", 2),
        };

        List<List<CommandConfig>> frames = FrameLayout.Group(commands);

        frames.Select(f => string.Join("+", f.Select(c => c.Code)))
            .Should().Equal("T1", "T2+T3", "T4", "T5");
    }

    [Fact]
    public void Counters_Survive_A_Restart_And_A_Broken_File_Starts_From_Zero()
    {
        string file = Path.Combine(_root, "counters.json");
        CounterStore store = new(file);
        store.Load().Should().BeEmpty();

        store.Save(new Dictionary<string, int[]> { ["T1"] = new[] { 10, 2, 1 } });

        Dictionary<string, int[]> loaded = new CounterStore(file).Load();
        loaded["t1"].Should().Equal(10, 2, 1);   // mã lệnh không phân biệt hoa thường
        File.WriteAllText(file, "{ broken", Encoding.UTF8);
        new CounterStore(file).Load().Should().BeEmpty();
        File.WriteAllText(file, "{ \"T1\": [1, 2] }", Encoding.UTF8);
        new CounterStore(file).Load().Should().BeEmpty();   // sai số phần tử thì bỏ
    }

    [Fact]
    public void Catalog_Lists_Every_Product_And_Shows_Broken_Ones_With_The_Error()
    {
        AppPaths paths = AppPaths.Resolve(_root, _root);
        paths.EnsureCreated();
        ProductPaths good = paths.Product("P-1");
        Directory.CreateDirectory(good.Jobs);
        File.WriteAllText(good.ProductFile, @"{ ""schemaVersion"": 1, ""id"": ""P-1"",
            ""name"": ""Cover"", ""commands"": [ { ""code"": ""T1"" }, { ""code"": ""T2"" } ] }");
        ProductPaths bad = paths.Product("P-2");
        Directory.CreateDirectory(bad.Jobs);
        File.WriteAllText(bad.ProductFile, "{ not json");
        Directory.CreateDirectory(Path.Combine(paths.Products, "empty-folder"));

        IReadOnlyList<ProductEntry> entries = ProductCatalog.List(paths);

        entries.Select(e => e.Id).Should().Equal("P-1", "P-2");
        entries[0].Name.Should().Be("Cover");
        entries[0].Commands.Should().Be(2);
        entries[0].Version.Should().MatchRegex("^[0-9a-f]{12}$");
        entries[0].Error.Should().BeEmpty();
        entries[1].Config.Should().BeNull();
        entries[1].Error.Should().NotBeEmpty();
    }

    [Fact]
    public void Relative_Data_Folder_Is_Beside_The_Exe_And_Images_Can_Live_On_Another_Drive()
    {
        string program = Path.Combine(_root, "MeoVision");

        AppPaths paths = AppPaths.Resolve("Data", program, @"E:\Image", "Results");

        paths.Root.Should().Be(Path.Combine(program, "Data"));
        paths.Images.Should().Be(@"E:\Image");
        paths.Results.Should().Be(Path.Combine(program, "Results"));
        paths.Products.Should().Be(Path.Combine(program, "Data", "products"));

        AppPaths defaults = AppPaths.Resolve("Data", program, "", "  ");
        defaults.Images.Should().Be(Path.Combine(program, "Data", "images"));
        defaults.Results.Should().Be(Path.Combine(program, "Data", "results"));
    }

    [Fact]
    public void Missing_Image_Drive_Falls_Back_Into_The_Data_Folder_With_A_Warning()
    {
        // Ổ chương trình tháo sang máy mới chưa có ổ D: — không được sập trước cả khi có log.
        char missing = "ZYXWVUTSRQPONMLKJIHG".First(d =>
            !DriveInfo.GetDrives().Any(x => char.ToUpperInvariant(x.Name[0]) == d));
        string program = Path.Combine(_root, "station");
        AppPaths paths = AppPaths.Resolve("Data", program, missing + @":\Image", null,
            missing + @":\Log");

        paths.EnsureCreated();

        paths.Images.Should().Be(Path.Combine(program, "Data", "images"));
        paths.Logs.Should().Be(Path.Combine(program, "Data", "logs"));
        Directory.Exists(paths.Logs).Should().BeTrue();
        paths.FallBacks.Select(m => m.Key).Should().Equal("startup.dataFolder.fallBack",
            "startup.dataFolder.fallBack");
        DataFolderCheck.Run(paths, 0).Status.Should().Be(StepStatus.Warning);
    }

    [Fact]
    public void Release_Hash_Skips_The_Data_Folder_Beside_The_Exe()
    {
        string program = Path.Combine(_root, "release");
        Directory.CreateDirectory(Path.Combine(program, ReleaseManifest.DataFolder, "images"));
        File.WriteAllText(Path.Combine(program, "MeoVision.exe"), "exe");
        File.WriteAllText(Path.Combine(program, ReleaseManifest.DataFolder, "images", "a.bmp"), "x");

        ReleaseInfo info = ReleaseManifest.Write(program, "2.0.0", "test");

        info.Files.Keys.Should().Equal("MeoVision.exe");
        File.WriteAllText(Path.Combine(program, ReleaseManifest.DataFolder, "new.dll"), "x");
        ReleaseManifest.Verify(program, info).Should().BeEmpty();
    }

    private static CommandConfig Command(string code, int slot) =>
        new() { Code = code, Display = new DisplayRecords { Slot = slot } };
}
