using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Cognex.VisionPro;
using Cognex.VisionPro.ImageProcessing;
using Cognex.VisionPro.Implementation.Internal;   // CogScriptSupport, CogScriptLanguageConstants
using Cognex.VisionPro.ToolBlock;
using FluentAssertions;
using MeoVision.Core.Products;
using MeoVision.Vision.Products;
using Xunit;
using Xunit.Abstractions;

namespace MeoVision.Vision.Tests;

/// <summary>Bộ kiểm job chạy THẬT trên VisionPro: tạo file .vpp, lưu bằng CogSerializer, mở lại.</summary>
public sealed class JobInspectorTests : IDisposable
{
    // Khung script C# cho ToolBlock (script của VisionPro 9.0 CR2 chỉ nhận C# 5 — Chương 12).
    private const string ScriptHead = @"
using System;
using Cognex.VisionPro;
using Cognex.VisionPro.ToolBlock;
public class CogToolBlockAdvancedScript : CogToolBlockAdvancedScriptBase
{
  public override bool GroupRun(ref string message, ref CogToolResultConstants result)
  {
";

    private const string ScriptTail = @"
  }
  public override void ModifyCurrentRunRecord(ICogRecord currentRecord) { }
  public override void ModifyLastRunRecord(ICogRecord lastRecord) { }
}";

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "mv-jobs-" +
        Guid.NewGuid().ToString("N"));
    private readonly ITestOutputHelper _output;

    public JobInspectorTests(ITestOutputHelper output)
    {
        _output = output;
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void Reads_Terminal_Names_And_Types_Of_A_Real_ToolBlock()
    {
        string file = Save("ok.vpp", NewBlock());

        JobInspection job = new CogJobInspector().Inspect(file);
        _output.WriteLine($"Inspect: {job.ElapsedMs} ms");

        job.LoadError.Should().BeNull();
        job.ScriptError.Should().BeNull();
        job.Inputs.Should().ContainSingle(t => t.Name == "InputImage" &&
                                               t.ValueType == typeof(CogImage8Grey));
        job.Outputs.Select(t => (t.Name, t.ValueType)).Should().Equal(
            ("Mean", typeof(double)), ("Pass", typeof(bool)));
    }

    [Fact]
    public void Script_That_Does_Not_Compile_Is_Reported()
    {
        CogToolBlock block = NewBlock();
        block.CreateNewScript(CogScriptLanguageConstants.ScriptCSharp,
            CogToolBlockScriptTypeConstants.Advanced);
        ArrayList references = block.Script.References;
        // Cú pháp C# 6 ($"...") — script compiler của 9.0 CR2 là C# 5 nên không biên dịch.
        block.Script = new CogScriptSupport(ScriptHead +
            "    message = $\"n={1}\"; return false;" + ScriptTail, references,
            CogScriptLanguageConstants.ScriptCSharp);
        string file = Save("script.vpp", block);

        // Đo thật: nạp file này VisionPro bật hộp thoại modal "Unable to load script" — không có
        // guard thì Inspect treo vô hạn (lượt chạy test đầu tiên của chương đã treo đúng chỗ này).
        Stopwatch watch = Stopwatch.StartNew();
        JobInspection job = new CogJobInspector().Inspect(file);

        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10));
        job.LoadError.Should().BeNull();
        job.ScriptError.Should().NotBeNull();
        _output.WriteLine($"{watch.ElapsedMilliseconds} ms: {job.ScriptError!.Args[0]}");
    }

    [Fact]
    public void Valid_Script_Is_Not_Reported_Even_Though_ScriptError_Is_Not_Empty()
    {
        CogToolBlock block = NewBlock();
        block.CreateNewScript(CogScriptLanguageConstants.ScriptCSharp,
            CogToolBlockScriptTypeConstants.Advanced);
        ArrayList references = block.Script.References;
        block.Script = new CogScriptSupport(ScriptHead + "    return true;" + ScriptTail,
            references, CogScriptLanguageConstants.ScriptCSharp);
        string file = Save("good-script.vpp", block);

        JobInspection job = new CogJobInspector().Inspect(file);

        job.ScriptError.Should().BeNull();
    }

    [Fact]
    public void File_Saved_As_A_Collection_Is_Not_A_ToolBlock()
    {
        // Cách một hệ thật lưu: ArrayList [ToolBlock, dữ liệu riêng] — mở bằng phần mềm khác là hỏng.
        string file = Path.Combine(_folder, "list.vpp");
        CogSerializer.SaveObjectToFile(new ArrayList { NewBlock(), new Dictionary<int, int>() },
            file);

        JobInspection job = new CogJobInspector().Inspect(file);

        job.LoadError!.Key.Should().Be("job.notToolBlock");
        job.LoadError.Args[1].Should().Be(typeof(ArrayList).FullName);
    }

    [Fact]
    public void File_That_Is_Not_A_Vpp_Fails_With_A_Message()
    {
        string file = Path.Combine(_folder, "broken.vpp");
        File.WriteAllText(file, "not a vpp");

        JobInspection job = new CogJobInspector().Inspect(file);

        job.LoadError!.Key.Should().Be("job.loadFailed");
        _output.WriteLine(job.LoadError.Args[1].ToString());
    }

    private static CogToolBlock NewBlock()
    {
        CogToolBlock block = new();
        block.Inputs.Add(new CogToolBlockTerminal("InputImage", typeof(CogImage8Grey)));
        block.Tools.Add(new CogHistogramTool { Name = "Histogram" });
        block.Outputs.Add(new CogToolBlockTerminal("Mean", typeof(double)));
        block.Outputs.Add(new CogToolBlockTerminal("Pass", typeof(bool)));
        return block;
    }

    private string Save(string name, CogToolBlock block)
    {
        string file = Path.Combine(_folder, name);
        CogSerializer.SaveObjectToFile(block, file);
        block.Dispose();
        return file;
    }
}
