using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using MeoVision.App.Devices;
using MeoVision.App.Devices.Lights;
using MeoVision.App.Hosting;
using MeoVision.App.Products;
using MeoVision.Core.Devices;
using MeoVision.Vision.Commands;
using MeoVision.Vision.Devices;
using MeoVision.Vision.Products;
using Xunit;
using Xunit.Abstractions;

namespace MeoVision.Vision.Tests;

/// <summary>
/// Sản phẩm mẫu đi kèm repo + machine.example.json phải qua được bộ kiểm THẬT (mở .vpp bằng
/// VisionPro) — như Chương 18 kiểm machine.example.json bằng các factory thật.
/// </summary>
public sealed class SampleProductTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mv-sample-" +
        Guid.NewGuid().ToString("N"));
    private readonly ITestOutputHelper _output;

    public SampleProductTests(ITestOutputHelper output) => _output = output;

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void Shipped_Sample_Product_Is_Valid_On_The_Example_Machine()
    {
        AppPaths paths = AppPaths.Resolve(_root, Path.GetTempPath());
        paths.EnsureCreated();
        CopyFolder(Path.Combine(AppContext.BaseDirectory, "samples", "products", "P-1024"),
            paths.Product("P-1024").Directory);
        CopyFolder(Path.Combine(AppContext.BaseDirectory, "samples", "machine", "calibration"),
            paths.MachineCalibration);

        MachineConfigLoader loader = new(new IDeviceFactory[]
        {
            new CognexGigECameraFactory(), new FolderCameraFactory(), new AsciiLightFactory(),
        });
        string machineFile = Path.Combine(AppContext.BaseDirectory, "machine.example.json");
        MachineConfig machine = loader.Load(machineFile).Config!;
        // Số kênh và cường độ tối đa của đèn lấy từ chính bộ điều khiển tạo theo cấu hình
        // (chưa mở cổng) — đúng như DeviceManager.LightLimits lúc chạy thật.
        Dictionary<string, (int, int)> lights = machine.Lights.ToDictionary(l => l.Role, l =>
        {
            using ILightController light = (ILightController)loader
                .FactoryFor(DeviceKind.Light, l.Type).Create(l, AppContext.BaseDirectory);
            return (light.ChannelCount, light.MaxIntensity);
        });
        MachineView view = new(machine.Cameras.Select(c => c.Role), lights,
            machine.Channels.Select(c => c.Id),
            id => Directory.Exists(Path.Combine(paths.MachineCalibration, id)));

        ProductLoadResult result = new ProductLoader(new CogJobInspector(),
                new CogCalibrationLoader(paths.MachineCalibration))
            .Load(paths.Product("P-1024"), "P-1024", view);

        _output.WriteLine($"job check: {result.JobCheckMs} ms");
        result.Problems.Should().BeEmpty();
        result.Warnings.Should().BeEmpty();
        result.Product!.Commands.Select(c => (c.Code, c.Channel)).Should().Equal(
            ("T1", "CH1"), ("T2", "CH1"), ("T3", "CH2"));
        result.Loaded!.Calibration("TopCal").Should().NotBeNull();
    }

    private static void CopyFolder(string source, string target)
    {
        foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            string to = Path.Combine(target, file.Substring(source.Length + 1));
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Copy(file, to, overwrite: true);
        }
    }
}
