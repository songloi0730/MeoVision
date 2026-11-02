using System;
using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using MeoVision.App.Hosting;
using MeoVision.App.Localization;
using MeoVision.Core.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MeoVision.App.Tests;

/// <summary>Bộ dịch dùng chung cho test (tiếng Anh = ngôn ngữ log).</summary>
internal static class TestText
{
    public static readonly ILocalizer English = new JsonLocalizer(
        new Dictionary<string, Dictionary<string, string>>
        {
            ["en-US"] = new() { ["step.timeout"] = "Timed out after {0:0} ms" },
        },
        "en-US", "en-US", NullLogger.Instance);
}

public class LocalizationTests
{
    private static JsonLocalizer Create(string language = "vi-VN") => new(
        new Dictionary<string, Dictionary<string, string>>
        {
            ["en-US"] = new()
            {
                ["disk"] = "{0:0.0} GB free",
                ["only.en"] = "English only",
                ["outer"] = "Startup: {0}",
                ["step.data"] = "Data folder",
            },
            ["vi-VN"] = new()
            {
                ["disk"] = "còn trống {0:0.0} GB",
                ["outer"] = "Khởi động: {0}",
                ["step.data"] = "Thư mục dữ liệu",
                ["broken"] = "thiếu tham số {1}",
            },
        },
        language, "en-US", NullLogger.Instance);

    [Fact]
    public void Shipped_Language_Files_Have_Same_Keys_And_Placeholders()
    {
        // CHÍNH các file đi kèm exe — bản dịch thiếu khoá hay lệch {0} thì build đỏ.
        Dictionary<string, Dictionary<string, string>> tables = JsonLocalizer.LoadTables(
            Path.Combine(AppContext.BaseDirectory, "languages"));

        tables.Keys.Should().Contain(new[] { "vi-VN", "en-US", "zh-CN" });
        LanguageFileCheck.Compare(tables, "en-US").Should().BeEmpty();
    }

    [Fact]
    public void Numbers_Are_Formatted_In_The_Display_Culture()
    {
        JsonLocalizer text = Create("vi-VN");

        text.Text("disk", 42.8).Should().Be("còn trống 42,8 GB");
        text.SetLanguage("en-US");
        text.Text("disk", 42.8).Should().Be("42.8 GB free");
    }

    [Fact]
    public void Missing_Key_Falls_Back_To_Fallback_Language_Then_To_Bracketed_Key()
    {
        JsonLocalizer text = Create("vi-VN");

        text.Text("only.en").Should().Be("English only");
        text.Text("nowhere").Should().Be("[nowhere]");
    }

    [Fact]
    public void Nested_Msg_Arguments_Are_Translated_Too()
    {
        Create("vi-VN").Text(Msg.Of("outer", Msg.Of("step.data")))
            .Should().Be("Khởi động: Thư mục dữ liệu");
    }

    [Fact]
    public void Bad_Translation_Format_Does_Not_Throw()
    {
        Create("vi-VN").Text("broken", "a").Should().Be("thiếu tham số {1}");
    }

    [Fact]
    public void Unknown_Language_Keeps_Current_And_Known_Language_Raises_Event()
    {
        JsonLocalizer text = Create("vi-VN");
        int changed = 0;
        text.LanguageChanged += (_, _) => changed++;

        text.SetLanguage("fr-FR");
        text.Language.Should().Be("vi-VN");
        text.SetLanguage("EN-us");
        text.Language.Should().Be("en-US");
        changed.Should().Be(1);
    }

    [Fact]
    public void Product_Folder_Is_Self_Contained_And_Rejects_Bad_Ids()
    {
        AppPaths paths = AppPaths.Resolve(@"D:\MeoVisionData", @"C:\x");

        ProductPaths product = paths.Product("PC-20230220");
        product.ProductFile.Should().Be(@"D:\MeoVisionData\products\PC-20230220\product.json");
        product.Job("Locate.vpp").Should()
            .Be(@"D:\MeoVisionData\products\PC-20230220\jobs\Locate.vpp");
        paths.Invoking(p => p.Product(@"..\other")).Should().Throw<ArgumentException>();
    }
}
