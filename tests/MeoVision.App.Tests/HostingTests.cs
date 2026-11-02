using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MeoVision.App.Hosting;
using MeoVision.Core.Localization;
using MeoVision.Core.Vision;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MeoVision.App.Tests;

public class HostingTests
{
    [Fact]
    public void AppPaths_Relative_Root_Is_Resolved_From_Program_Directory()
    {
        AppPaths paths = AppPaths.Resolve("data", @"C:\Program\MeoVision");

        paths.Root.Should().Be(@"C:\Program\MeoVision\data");
        paths.Logs.Should().Be(@"C:\Program\MeoVision\data\logs");
    }

    [Fact]
    public void AppPaths_Expands_Environment_Variables()
    {
        AppPaths paths = AppPaths.Resolve(@"%ProgramData%\MeoVision", @"C:\x");

        paths.Root.Should().Be(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "MeoVision"));
    }

    [Fact]
    public void EnsureCreated_And_DataFolderCheck_Pass_On_Writable_Folder()
    {
        string root = Path.Combine(Path.GetTempPath(), "meovision-test-" + Guid.NewGuid());
        try
        {
            AppPaths paths = AppPaths.Resolve(root, @"C:\x");
            paths.EnsureCreated();

            Directory.Exists(paths.Backup).Should().BeTrue();
            DataFolderCheck.Run(paths, minFreeGb: 0).Status.Should().Be(StepStatus.Ok);
            DataFolderCheck.Run(paths, minFreeGb: 1e9).Status.Should().Be(StepStatus.Warning);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Shutdown_Continues_After_A_Failing_Or_Hanging_Step()
    {
        List<string> done = new();
        IReadOnlyList<StepResult> results = await new ShutdownSequence(
                NullLogger.Instance, TestText.English)
            .Add("Dung nhan lenh", TimeSpan.FromSeconds(1),
                () => throw new InvalidOperationException("socket da dong"))
            .Add("Tat den", TimeSpan.FromMilliseconds(200), () => Thread.Sleep(3000))
            .Add("Tra phan cung", TimeSpan.FromSeconds(1), () => done.Add("tra phan cung"))
            .RunAsync();

        results[0].Status.Should().Be(StepStatus.Failed);
        results[1].Status.Should().Be(StepStatus.Failed);
        results[2].Status.Should().Be(StepStatus.Ok);
        done.Should().Equal("tra phan cung");
    }

    [Fact]
    public void SingleInstanceGuard_Second_Instance_Is_Not_Owner()
    {
        string id = "MeoVisionTest-" + Guid.NewGuid();
        using SingleInstanceGuard first = SingleInstanceGuard.Acquire(id);
        bool secondIsOwner = true;

        // Mutex cho phép CÙNG luồng lấy lại — phải thử từ luồng khác mới giống tiến trình
        // thứ hai.
        Thread other = new(() =>
        {
            using SingleInstanceGuard second = SingleInstanceGuard.Acquire(id);
            secondIsOwner = second.IsOwner;
        });
        other.Start();
        other.Join();

        first.IsOwner.Should().BeTrue();
        secondIsOwner.Should().BeFalse();
    }

    [Fact]
    public void RunMarker_Detects_A_Run_That_Did_Not_Shut_Down_Cleanly()
    {
        string file = Path.Combine(Path.GetTempPath(), "meovision-test-" + Guid.NewGuid() +
            ".marker");
        try
        {
            RunMarker.Begin(file).PreviousRunEndedAbnormally.Should().BeFalse("lần chạy đầu");

            // Lần 1 "crash": không gọi EndCleanly → lần 2 phải nhận ra.
            RunMarker second = RunMarker.Begin(file);
            second.PreviousRunEndedAbnormally.Should().BeTrue();
            second.PreviousRun.Should().Contain("pid");

            // Lần 2 tắt đúng quy trình → lần 3 sạch.
            second.EndCleanly();
            RunMarker.Begin(file).PreviousRunEndedAbnormally.Should().BeFalse();
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void VisionRuntimeCheck_Fails_When_A_Required_Feature_Is_Missing()
    {
        FakeRuntime runtime = new("VxPatMax", "VxBlob");

        VisionRuntimeCheck.Run(runtime, new[] { "VxPatMax" }).Status.Should().Be(StepStatus.Ok);
        StepOutcome missing = VisionRuntimeCheck.Run(runtime, new[] { "VxPatMax", "VxCalib" });
        missing.Status.Should().Be(StepStatus.Failed);
        missing.Message.Args.Should().Contain("VxCalib");
    }

    private sealed class FakeRuntime : IVisionRuntime
    {
        private readonly string[] _features;

        public FakeRuntime(params string[] features) => _features = features;

        public VisionRuntimeInfo Probe(IReadOnlyCollection<string> requiredFeatures)
        {
            List<string> missing = new();
            foreach (string f in requiredFeatures)
            {
                if (Array.IndexOf(_features, f) < 0)
                {
                    missing.Add(f);
                }
            }
            return new VisionRuntimeInfo("Fake", "1.0", _features, missing);
        }

        public void ReleaseHardware()
        {
        }
    }
}
