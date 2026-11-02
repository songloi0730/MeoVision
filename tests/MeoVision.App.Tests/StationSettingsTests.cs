using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FluentAssertions;
using MeoVision.App.Hosting;
using Newtonsoft.Json.Linq;
using Xunit;

namespace MeoVision.App.Tests;

/// <summary>
/// Hai lớp cấu hình (DESIGN_LOG [27]): mặc định của bản phát hành + thông số riêng của trạm. Cài bản
/// mới không được làm mất thông số đã chỉnh tại máy; file trạm hỏng không được bị ghi đè.
/// </summary>
public sealed class StationSettingsTests : IDisposable
{
    private const string Shipped = @"{ ""MeoVision"": {
  ""DataRoot"": ""Data"", ""ImagesRoot"": """", ""LogsRoot"": """", ""MinFreeDiskGb"": 20,
  ""Results"": { ""SaveNgImages"": true, ""SaveOkImagesEvery"": 0 },
  ""Retention"": { ""KeepImageDays"": 30, ""ImageMinFreeGb"": 30 } } }";

    private readonly string _program = Path.Combine(Path.GetTempPath(), "mv-settings-" +
        Guid.NewGuid().ToString("N"));

    public StationSettingsTests()
    {
        Directory.CreateDirectory(_program);
        File.WriteAllText(Path.Combine(_program, StationSettings.ProgramFile), Shipped);
    }

    public void Dispose()
    {
        try { Directory.Delete(_program, recursive: true); } catch (IOException) { }
    }

    private string StationFile => Path.Combine(_program, "Data", StationSettings.FileName);

    [Fact]
    public void Without_A_Station_File_The_Shipped_Defaults_Are_Used()
    {
        LoadedSettings loaded = StationSettings.Load(_program);

        loaded.Problem.Should().BeNull();
        loaded.Settings.Retention.KeepImageDays.Should().Be(30);
        loaded.Paths.Images.Should().Be(Path.Combine(_program, "Data", "images"));
        loaded.StationFile.Should().Be(StationFile);
    }

    [Fact]
    public void Save_Writes_Only_What_Differs_And_A_New_Release_Keeps_It()
    {
        AppSettings edited = StationSettings.Copy(StationSettings.Load(_program).Settings);
        edited.ImagesRoot = @"D:\MeoVisionData\Images";
        edited.Retention.ImageMinFreeGb = 3;

        IReadOnlyDictionary<string, (string Old, string New)> changes =
            StationSettings.Save(_program, StationFile, edited);

        changes.Keys.Should().BeEquivalentTo("ImagesRoot", "Retention.ImageMinFreeGb");
        JObject file = JObject.Parse(File.ReadAllText(StationFile));
        file["MeoVision"]!.ToString(Newtonsoft.Json.Formatting.None).Should()
            .Be(@"{""ImagesRoot"":""D:\\MeoVisionData\\Images"",""Retention"":{""ImageMinFreeGb"":3.0}}");

        // Bản phát hành mới: đổi mặc định khác, thêm mục mới — thông số của trạm vẫn còn.
        File.WriteAllText(Path.Combine(_program, StationSettings.ProgramFile),
            Shipped.Replace(@"""KeepImageDays"": 30", @"""KeepImageDays"": 45")
                .Replace(@"""MinFreeDiskGb"": 20", @"""MinFreeDiskGb"": 25"));
        LoadedSettings after = StationSettings.Load(_program);
        after.Settings.ImagesRoot.Should().Be(@"D:\MeoVisionData\Images");
        after.Settings.Retention.ImageMinFreeGb.Should().Be(3);
        after.Settings.Retention.KeepImageDays.Should().Be(45);   // mặc định mới của bản mới
        after.Settings.MinFreeDiskGb.Should().Be(25);
        after.Paths.Images.Should().Be(@"D:\MeoVisionData\Images");
    }

    [Fact]
    public void Saving_Again_Keeps_The_Previous_File_As_Bak_And_Back_To_Default_Removes_The_Key()
    {
        AppSettings edited = StationSettings.Copy(StationSettings.Load(_program).Settings);
        edited.LogsRoot = @"D:\Logs";
        StationSettings.Save(_program, StationFile, edited);
        edited.LogsRoot = string.Empty;

        StationSettings.Save(_program, StationFile, edited)
            .Should().ContainKey("LogsRoot").WhoseValue.Should().Be(("\"D:\\\\Logs\"", "\"\""));

        JObject.Parse(File.ReadAllText(StationFile))["MeoVision"]!.HasValues.Should().BeFalse();
        File.ReadAllText(StationFile + ".bak").Should().Contain("Logs");
    }

    [Fact]
    public void Broken_Station_File_Falls_Back_To_Defaults_And_Is_Not_Overwritten()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StationFile)!);
        File.WriteAllText(StationFile, "{ \"MeoVision\": { \"ImagesRoot\": \"D:\\x\" ", Encoding.UTF8);

        LoadedSettings loaded = StationSettings.Load(_program);

        loaded.Problem!.Key.Should().Be("startup.settings.invalid");
        loaded.Settings.ImagesRoot.Should().BeEmpty();
        File.ReadAllText(StationFile).Should().Contain("D:\\x");
    }

    [Fact]
    public void Unknown_Keys_Are_Ignored_And_DataRoot_Cannot_Move_Itself()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StationFile)!);
        File.WriteAllText(StationFile,
            @"{ ""MeoVision"": { ""DataRoot"": ""E:\\Other"", ""FromANewerVersion"": 1,
                ""Results"": { ""SaveOkImagesEvery"": 50 } } }");

        LoadedSettings loaded = StationSettings.Load(_program);

        loaded.Problem.Should().BeNull();
        loaded.Settings.DataRoot.Should().Be("Data");
        loaded.Settings.Results.SaveOkImagesEvery.Should().Be(50);
        loaded.Settings.Results.SaveNgImages.Should().BeTrue();   // mục cùng nhóm vẫn mặc định
    }

    [Fact]
    public void Data_Folder_Inside_OneDrive_Gives_A_Warning()
    {
        string cloud = Path.Combine(_program, "OneDriveRoot");
        string data = Path.Combine(cloud, "Station", "Data");
        Directory.CreateDirectory(data);
        string? saved = Environment.GetEnvironmentVariable("OneDriveCommercial");
        Environment.SetEnvironmentVariable("OneDriveCommercial", cloud);
        try
        {
            DataFolderCheck.CloudSyncedRoot(data).Should().Be(cloud);
            DataFolderCheck.Run(AppPaths.Resolve(data, _program), 0).Message.Key
                .Should().Be("startup.dataFolder.cloudSynced");
            DataFolderCheck.CloudSyncedRoot(_program).Should().BeNull();
        }
        finally
        {
            Environment.SetEnvironmentVariable("OneDriveCommercial", saved);
        }
    }
}
