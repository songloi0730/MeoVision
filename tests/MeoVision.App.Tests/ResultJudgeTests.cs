using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using MeoVision.App.Commands;
using MeoVision.Core.Commands;
using MeoVision.Core.Products;
using Xunit;

namespace MeoVision.App.Tests;

/// <summary>Quy tắc kết luận (Chương 20, mục 20.5) — hàm thuần, không thiết bị.</summary>
public sealed class ResultJudgeTests
{
    private static CommandConfig Command(params OutputConfig[] outputs) => new()
    {
        Code = "T1",
        Job = "T1.vpp",
        Outputs = outputs.ToList(),
    };

    private static JobRun Run(JobRunStatus status, params (string, object?)[] values) =>
        new(status, "message", values.ToDictionary(v => v.Item1, v => v.Item2), 1);

    [Theory]
    [InlineData(0.0504, "0.050", true)]    // làm tròn xuống đúng biên → trong giới hạn
    [InlineData(0.0506, "0.051", false)]   // làm tròn lên vượt biên → NG
    public void Limits_Are_Checked_On_The_Value_The_Machine_Receives(double raw, string text,
        bool inLimits)
    {
        CommandConfig command = Command(new OutputConfig
        {
            Terminal = "Gap", Name = "Gap", Format = "0.000", Upper = 0.050,
        });

        Judgement judgement = ResultJudge.Judge(command, Run(JobRunStatus.Accept, ("Gap", raw)));

        judgement.Outputs.Single().Text.Should().Be(text);
        judgement.Outputs.Single().InLimits.Should().Be(inLimits);
        judgement.Status.Should().Be(inLimits ? CommandStatus.Ok : CommandStatus.Ng);
    }

    [Fact]
    public void Missing_Value_Is_Ng_When_The_Job_Rejected_But_Error_When_It_Accepted()
    {
        CommandConfig command = Command(new OutputConfig { Terminal = "X", Name = "X" });

        ResultJudge.Judge(command, Run(JobRunStatus.Reject, ("X", null)))
            .Status.Should().Be(CommandStatus.Ng);
        Judgement accepted = ResultJudge.Judge(command, Run(JobRunStatus.Accept, ("X", null)));
        accepted.Status.Should().Be(CommandStatus.Error);
        accepted.Error!.Key.Should().Be("cmd.outputMissing");
    }

    [Fact]
    public void Job_Error_NaN_And_Unsendable_Values_Are_Errors()
    {
        CommandConfig command = Command(new OutputConfig { Terminal = "X", Name = "X" });

        ResultJudge.Judge(command, Run(JobRunStatus.Error, ("X", 1.0)))
            .Error!.Key.Should().Be("cmd.jobError");
        ResultJudge.Judge(command, Run(JobRunStatus.Accept, ("X", double.NaN)))
            .Error!.Key.Should().Be("cmd.outputInvalid");
        ResultJudge.Judge(command, Run(JobRunStatus.Accept, ("X", new object())))
            .Error!.Key.Should().Be("cmd.outputType");
    }

    [Fact]
    public void Bool_Becomes_1_Or_0_And_Text_Passes_Through()
    {
        CommandConfig command = Command(new OutputConfig { Terminal = "Ok", Name = "Ok" },
            new OutputConfig { Terminal = "Code", Name = "Code" });

        Judgement judgement = ResultJudge.Judge(command,
            Run(JobRunStatus.Accept, ("Ok", true), ("Code", "AB12")));

        judgement.Outputs.Select(o => o.Text).Should().Equal("1", "AB12");
        judgement.Status.Should().Be(CommandStatus.Ok);
    }
}
