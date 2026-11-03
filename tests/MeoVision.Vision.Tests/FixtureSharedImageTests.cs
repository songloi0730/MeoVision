using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cognex.VisionPro;
using Cognex.VisionPro.ImageFile;
using Cognex.VisionPro.ToolBlock;
using FluentAssertions;
using MeoVision.Core.Commands;
using MeoVision.Core.Devices;
using MeoVision.Vision.Commands;
using MeoVision.Vision.Devices;
using Xunit;

namespace MeoVision.Vision.Tests;

/// <summary>
/// Hai đối tượng job (job đang chạy + bản nháp) có Fixture, chạy lần lượt trên CÙNG một ảnh — tình huống
/// của chạy kèm và vòng ảnh gần nhất (DESIGN_LOG [31]). Job mẫu C1: PMAlign → Fixture → đo.
/// </summary>
public class FixtureSharedImageTests
{
    private static readonly string Dir = Path.Combine(AppContext.BaseDirectory, "samples", "toolchains");

    private static readonly string[] Outputs = { "Width", "Height", "Pitch", "HoleDiameter" };

    private static CogVisionJob Job() => new(
        (CogToolBlock)CogSerializer.LoadObjectFromFile(Path.Combine(Dir, "C1_measure.vpp")),
        "C1_measure.vpp", JobContract.InputImage);

    private static CameraImage Image()
    {
        using CogImageFile file = new();
        file.Open(Path.Combine(Dir, "ok_03.bmp"), CogImageFileModeConstants.Read);
        return new CogCameraImage(file[0], "ok_03.bmp", DateTime.Now);
    }

    [Fact]
    public void Second_Job_Object_Fails_On_The_Same_Image_But_Not_On_Its_Own_Copy()
    {
        using CogVisionJob running = Job();
        using CogVisionJob draft = Job();
        using CameraImage image = Image();

        JobRun first = running.Run(image, Outputs);
        first.Status.Should().Be(JobRunStatus.Accept);

        // Hành vi VisionPro đã đo: Fixture của đối tượng khác không được sửa hệ "Fixture" có sẵn.
        draft.Run(image, Outputs).Status.Should().Be(JobRunStatus.Error);

        using CameraImage copy = image.CopyForAnotherJob()!;
        JobRun second = draft.Run(copy, Outputs);
        second.Status.Should().Be(JobRunStatus.Accept);
        foreach (string name in Outputs)
        {
            second.Outputs[name].Should().Be(first.Outputs[name], name);
        }
    }

    [Fact]
    public void Copy_Keeps_Existing_Spaces_But_Not_Spaces_Added_Later()
    {
        using CameraImage image = Image();
        ICogImage cog = CogCameraImage.From(image);
        cog.CoordinateSpaceTree.AddSpace("@", "calib", new CogTransform2DLinear(), true,
            CogAddSpaceConstants.ReplaceDuplicate);

        using CameraImage copy = image.CopyForAnotherJob()!;
        cog.CoordinateSpaceTree.AddSpace("@", "later", new CogTransform2DLinear(), true,
            CogAddSpaceConstants.ReplaceDuplicate);

        List<string> names = CogCameraImage.From(copy).CoordinateSpaceTree.AllChildNames(
            "@", CogSpaceTreePathnameConstants.Full, false).Cast<string>().ToList();
        names.Should().Contain(@"@\calib", "hiệu chuẩn của máy phải đi theo bản sao");
        names.Should().NotContain(@"@\later", "cây hệ toạ độ của bản sao phải tách riêng");
    }
}
