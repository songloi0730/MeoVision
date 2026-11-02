using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MeoVision.App.Hosting;
using MeoVision.Core.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MeoVision.App.Tests;

public class StartupSequenceTests
{
    private static readonly TimeSpan OneSecond = TimeSpan.FromSeconds(1);

    private static StartupSequence NewSequence() => new(NullLogger.Instance, TestText.English);

    [Fact]
    public async Task Runs_Steps_In_Order_And_Allows_Production_When_All_Ok()
    {
        List<string> order = new();
        StartupReport report = await NewSequence()
            .Add("A", true, OneSecond, _ => { order.Add("A"); return Ok(); })
            .Add("B", true, OneSecond, _ => { order.Add("B"); return Ok(); })
            .RunAsync();

        order.Should().Equal("A", "B");
        report.CanRunProduction.Should().BeTrue();
    }

    [Fact]
    public async Task Critical_Failure_Skips_Remaining_Steps_And_Blocks_Production()
    {
        bool laterStepRan = false;
        StartupReport report = await NewSequence()
            .Add("Thu muc", true, OneSecond, _ => Failed())
            .Add("Camera", true, OneSecond, _ => { laterStepRan = true; return Ok(); })
            .RunAsync();

        laterStepRan.Should().BeFalse();
        report.Steps[1].Status.Should().Be(StepStatus.Skipped);
        report.CanRunProduction.Should().BeFalse();
    }

    [Fact]
    public async Task NonCritical_Failure_Becomes_Warning_And_Does_Not_Block()
    {
        StartupReport report = await NewSequence()
            .Add("Camera phu", false, OneSecond, _ => Failed())
            .Add("Camera chinh", true, OneSecond, _ => Ok())
            .RunAsync();

        report.Steps[0].Status.Should().Be(StepStatus.Warning);
        report.Steps[1].Status.Should().Be(StepStatus.Ok);
        report.CanRunProduction.Should().BeTrue();
    }

    [Fact]
    public async Task Exception_In_Step_Becomes_Failed_With_Message()
    {
        StartupReport report = await NewSequence()
            .Add("Den", true, OneSecond, _ => throw new InvalidOperationException("COM3 ban"))
            .RunAsync();

        report.Steps[0].Status.Should().Be(StepStatus.Failed);
        report.Steps[0].Message.Key.Should().Be("step.exception");
        report.Steps[0].Message.Args.Should().Contain("COM3 ban");
    }

    [Fact]
    public async Task Step_That_Ignores_Cancellation_Still_Times_Out()
    {
        // Mô phỏng một lệnh SDK đồng bộ không nghe CancellationToken: chặn luồng 3 giây.
        StartupReport report = await NewSequence()
            .Add("SDK treo", true, TimeSpan.FromMilliseconds(200), _ =>
            {
                Thread.Sleep(3000);
                return Ok();
            })
            .RunAsync();

        report.Steps[0].Status.Should().Be(StepStatus.Failed);
        report.Steps[0].Message.Key.Should().Be("step.timeout");
        report.Steps[0].ElapsedMs.Should().BeLessThan(2000);
    }

    private static Task<StepOutcome> Ok() => Task.FromResult(StepOutcome.Ok());

    private static Task<StepOutcome> Failed() => Task.FromResult(StepOutcome.Failed(Msg.Of("x")));
}
