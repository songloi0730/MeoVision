using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cognex.VisionPro;
using Cognex.VisionPro.Blob;
using Cognex.VisionPro.ImageFile;
using Cognex.VisionPro.Implementation.Internal;   // CogScriptSupport
using Cognex.VisionPro.ToolBlock;
using FluentAssertions;
using MeoVision.Core.Commands;
using MeoVision.Core.Devices;
using MeoVision.Core.Products;
using MeoVision.Vision.Commands;
using MeoVision.Vision.Devices;
using MeoVision.Vision.Products;
using Xunit;

namespace MeoVision.Vision.Tests;

/// <summary>
/// Chương 22 chạy THẬT trên VisionPro: tham số nhanh vào terminal input (đổi đúng kiểu), record
/// hiển thị còn đúng sau lần chạy kế tiếp, và job sửa trong bản nháp ghi ra file mở lại được.
/// </summary>
public sealed class DraftRunTests : IDisposable
{
    private static readonly string Images = @"C:\Program Files\Cognex\VisionPro\Images";

    // Tham số nhanh kiểu int: số đốm nhỏ nhất. Script đọc terminal input và đặt vào tool (C# 5).
    private const string Script = @"
using System;
using Cognex.VisionPro;
using Cognex.VisionPro.ToolBlock;
using Cognex.VisionPro.Blob;
public class CogToolBlockAdvancedScript : CogToolBlockAdvancedScriptBase
{
  private CogToolBlock mToolBlock;
  public override void Initialize(Cognex.VisionPro.ToolGroup.CogToolGroup host)
  { base.Initialize(host); mToolBlock = (CogToolBlock)host; }
  public override void ModifyCurrentRunRecord(ICogRecord currentRecord) { }
  public override void ModifyLastRunRecord(ICogRecord lastRecord) { }
  public override bool GroupRun(ref string message, ref CogToolResultConstants result)
  {
    CogBlobTool b = (CogBlobTool)mToolBlock.Tools[""Blob""];
    b.InputImage = (CogImage8Grey)mToolBlock.Inputs[""InputImage""].Value;
    b.RunParams.ConnectivityMinPixels = (int)mToolBlock.Inputs[""MinPixels""].Value;
    mToolBlock.RunTool(b, ref message, ref result);
    mToolBlock.Outputs[""Count""].Value = b.Results.GetBlobs().Count;
    return false;
  }
}";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "mv-draftrun-" +
        Guid.NewGuid().ToString("N"));

    public DraftRunTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void Tunable_Value_Goes_Into_The_Int_Input_Terminal()
    {
        using CogVisionJob job = new(TunableJob(), "tune.vpp", JobContract.InputImage);
        using CameraImage blobs = Load("blobs.bmp");

        int Count(double minPixels) => (int)job.Run(blobs, Request(minPixels, false))
            .Outputs["Count"]!;

        int loose = Count(20);
        int strict = Count(2000);

        loose.Should().Be(29);                 // cùng số với job Ch20 đặt 20 cố định
        strict.Should().BeLessThan(loose);     // 20.0 (double, product.json) → int của terminal
        Count(20).Should().Be(loose);          // giá trị không "dính" từ lần trước
    }

    [Fact]
    public void Record_Still_Shows_Its_Own_Image_After_The_Next_Run()
    {
        using CogVisionJob job = new(TunableJob(), "tune.vpp", JobContract.InputImage);
        using CameraImage first = Load("blobs.bmp");
        using CameraImage second = Load("CheckCal.idb");

        ICogRecord record = (ICogRecord)job.Run(first, Request(20, true)).Record!;
        job.Run(second, Request(20, true));

        ICogImage shown = Contents(record).OfType<ICogImage>().First();
        shown.Should().BeSameAs(CogCameraImage.From(first));
        job.Run(first, Request(20, false)).Record.Should().BeNull();
    }

    [Fact]
    public void Saved_Draft_Job_Reopens_With_The_Same_Terminals_And_Answer()
    {
        string file = Path.Combine(_root, "tune.vpp");
        using CameraImage blobs = Load("blobs.bmp");
        using (CogVisionJob job = new(TunableJob(), "tune.vpp", JobContract.InputImage))
        {
            ((CogToolBlock)job.EditSubject).Tools["Blob"].Should().NotBeNull();
            job.Save(file);
            job.Save(file);   // ghi đè qua file tạm + đổi tên
        }

        JobInspection inspected = new CogJobInspector().Inspect(file);
        using IVisionJob reopened = inspected.Job!;

        inspected.LoadError.Should().BeNull();
        inspected.Inputs.Single(t => t.Name == "MinPixels").ValueType.Should().Be(typeof(int));
        reopened.Run(blobs, Request(20, false)).Outputs["Count"].Should().Be(29);
        Directory.GetFiles(_root).Should().Equal(file);   // không còn file .tmp
    }

    private static JobRequest Request(double minPixels, bool record) =>
        new(new[] { "Count" }, new Dictionary<string, double> { ["MinPixels"] = minPixels },
            record);

    private static IEnumerable<object> Contents(ICogRecord record)
    {
        for (int i = 0; i < record.SubRecords.Count; i++)
        {
            ICogRecord sub = record.SubRecords[i];
            if (sub.Content is not null)
            {
                yield return sub.Content;
            }
            foreach (object inner in Contents(sub))
            {
                yield return inner;
            }
        }
    }

    private static CogToolBlock TunableJob()
    {
        CogToolBlock block = new();
        block.Inputs.Add(new CogToolBlockTerminal("InputImage", typeof(CogImage8Grey)));
        block.Inputs.Add(new CogToolBlockTerminal("MinPixels", 20));
        block.Outputs.Add(new CogToolBlockTerminal("Count", typeof(int)));
        block.Tools.Add(new CogBlobTool { Name = "Blob" });
        block.CreateNewScript(CogScriptLanguageConstants.ScriptCSharp,
            CogToolBlockScriptTypeConstants.Advanced);
        ArrayList references = block.Script.References;
        block.Script = new CogScriptSupport(Script, references,
            CogScriptLanguageConstants.ScriptCSharp);
        return block;
    }

    private static CameraImage Load(string name)
    {
        using CogImageFile file = new();
        file.Open(Path.Combine(Images, name), CogImageFileModeConstants.Read);
        ICogImage image = file[0];
        if (image is not CogImage8Grey)
        {
            image = CogImageConvert.GetIntensityImage(image, 0, 0, image.Width, image.Height);
        }
        return new CogCameraImage(image, name, DateTime.Now);
    }
}
