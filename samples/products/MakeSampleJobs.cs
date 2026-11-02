// Tạo job mẫu cho sản phẩm P-1024 (Chương 19–20) và hiệu chuẩn mẫu của máy. Chạy một lần khi
// cần tạo lại file .vpp:
//   csc /platform:x64 /r:<VisionPro>\Cognex.VisionPro.dll /r:...Core.dll /r:...ToolGroup.dll
//       /r:...ImageProcessing.dll /r:...Blob.dll /r:...CalibFix.dll MakeSampleJobs.cs
//   MakeSampleJobs.exe <thư mục jobs> <thư mục machine\calibration>
// Mỗi job theo hợp đồng của MeoVision: input "InputImage", output có tên; script C# 5 (script
// compiler của VisionPro 9.0 CR2 — Chương 12) chạy tool rồi gán output.
using System;
using System.Collections;
using System.IO;
using Cognex.VisionPro;
using Cognex.VisionPro.Blob;
using Cognex.VisionPro.CalibFix;
using Cognex.VisionPro.ImageProcessing;
using Cognex.VisionPro.Implementation.Internal;
using Cognex.VisionPro.ToolBlock;

internal static class MakeSampleJobs
{
    private const string Head = @"
using System;
using Cognex.VisionPro;
using Cognex.VisionPro.ToolBlock;
using Cognex.VisionPro.ImageProcessing;
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
    ICogImage image = (ICogImage)mToolBlock.Inputs[""InputImage""].Value;
";

    private const string Tail = @"
    return false;
  }
}";

    private static void Main(string[] args)
    {
        string jobs = args.Length > 0 ? args[0] : ".";
        Directory.CreateDirectory(jobs);

        CogToolBlock mean = NewBlock();
        mean.Tools.Add(new CogHistogramTool { Name = "Histogram" });
        mean.Outputs.Add(new CogToolBlockTerminal("Mean", typeof(double)));
        SetScript(mean, @"
    CogHistogramTool h = (CogHistogramTool)mToolBlock.Tools[""Histogram""];
    h.InputImage = (CogImage8Grey)image;
    mToolBlock.RunTool(h, ref message, ref result);
    mToolBlock.Outputs[""Mean""].Value = h.Result == null ? double.NaN : h.Result.Mean;");
        Save(mean, Path.Combine(jobs, "T1_mean.vpp"));

        // Chương 22: "MinArea" là terminal INPUT kiểu int — tham số nhanh khai trong product.json,
        // kỹ sư kéo thanh trượt trên bản nháp mà không mở job.
        CogToolBlock blobs = NewBlock();
        blobs.Inputs.Add(new CogToolBlockTerminal("MinArea", 20));
        blobs.Tools.Add(new CogBlobTool { Name = "Blob" });
        blobs.Outputs.Add(new CogToolBlockTerminal("Count", typeof(int)));
        SetScript(blobs, @"
    CogBlobTool b = (CogBlobTool)mToolBlock.Tools[""Blob""];
    b.RunParams.ConnectivityMinPixels = (int)mToolBlock.Inputs[""MinArea""].Value;
    b.InputImage = (CogImage8Grey)image;
    mToolBlock.RunTool(b, ref message, ref result);
    mToolBlock.Outputs[""Count""].Value = b.Results == null ? 0 : b.Results.GetBlobs().Count;");
        Save(blobs, Path.Combine(jobs, "T3_blobs.vpp"));

        // Chương 20: vị trí đốm lớn nhất. Blob báo toạ độ trong hệ đang CHỌN của ảnh — lệnh có
        // hiệu chuẩn thì ảnh tới với "@\Calibrated" đã chọn, nên X/Y ra mm mà job không phải biết.
        CogToolBlock spot = NewBlock();
        spot.Inputs.Add(new CogToolBlockTerminal("MinArea", 20));
        spot.Tools.Add(new CogBlobTool { Name = "Blob" });
        spot.Outputs.Add(new CogToolBlockTerminal("X", typeof(double)));
        spot.Outputs.Add(new CogToolBlockTerminal("Y", typeof(double)));
        SetScript(spot, @"
    CogBlobTool b = (CogBlobTool)mToolBlock.Tools[""Blob""];
    b.RunParams.ConnectivityMinPixels = (int)mToolBlock.Inputs[""MinArea""].Value;
    b.InputImage = (CogImage8Grey)image;
    mToolBlock.RunTool(b, ref message, ref result);
    if (b.Results == null || b.Results.GetBlobs().Count == 0)
    {
      result = CogToolResultConstants.Reject;
      return false;
    }
    CogBlobResult largest = b.Results.GetBlobs()[0];
    foreach (CogBlobResult r in b.Results.GetBlobs())
    {
      if (r.Area > largest.Area) { largest = r; }
    }
    mToolBlock.Outputs[""X""].Value = largest.CenterOfMassX;
    mToolBlock.Outputs[""Y""].Value = largest.CenterOfMassY;");
        Save(spot, Path.Combine(jobs, "T2_spot.vpp"));

        if (args.Length > 1)
        {
            SaveCalibration(Path.Combine(args[1], "TopCal"));
        }
    }

    // Hiệu chuẩn N điểm mẫu: 0,05 mm/pixel, gốc (10; 20) mm — số tròn để dễ kiểm bằng mắt.
    // Máy thật lấy các cặp điểm từ bàn cờ hoặc từ máy dẫn (Chương 7, Chương 21).
    private static void SaveCalibration(string folder)
    {
        Directory.CreateDirectory(folder);
        CogCalibNPointToNPoint calibration = new CogCalibNPointToNPoint();
        double[] pixels = { 0, 1000 };
        foreach (double x in pixels)
        {
            foreach (double y in pixels)
            {
                calibration.AddPointPair(x, y, 10 + 0.05 * x, 20 + 0.05 * y);
            }
        }
        calibration.Calibrate();
        CogCalibNPointToNPointTool tool = new CogCalibNPointToNPointTool();
        tool.Calibration = calibration;
        string file = Path.Combine(folder, "calibration.vpp");
        CogSerializer.SaveObjectToFile(tool, file);
        Console.WriteLine(file + "  " + new FileInfo(file).Length + " bytes");
    }

    private static CogToolBlock NewBlock()
    {
        CogToolBlock block = new CogToolBlock();
        block.Inputs.Add(new CogToolBlockTerminal("InputImage", typeof(CogImage8Grey)));
        return block;
    }

    private static void SetScript(CogToolBlock block, string body)
    {
        block.CreateNewScript(CogScriptLanguageConstants.ScriptCSharp,
            CogToolBlockScriptTypeConstants.Advanced);
        ArrayList references = block.Script.References;
        // Tham chiếu mặc định của script chỉ gồm assembly của các tool ĐANG có trong block (thử
        // thật: block có Histogram thì có ImageProcessing nhưng không có Blob, và ngược lại). Khung
        // script dùng chung cả hai nên bổ sung cái còn thiếu, theo kiểu đường dẫn "VisionPro"
        // (%VPRO_ROOT%\ReferencedAssemblies) để file chạy được trên máy khác.
        string[] needed = { "Cognex.VisionPro.ImageProcessing.dll", "Cognex.VisionPro.Blob.dll" };
        foreach (string name in needed)
        {
            bool present = false;
            foreach (CogReferencedAssembly r in references)
            {
                if (r.Name == name) { present = true; }
            }
            if (!present)
            {
                references.Add(new CogReferencedAssembly(name, @"%VPRO_ROOT%\ReferencedAssemblies",
                    CogReferencedAssemblyPathConstants.VisionPro));
            }
        }
        block.Script = new CogScriptSupport(Head + body + Tail, references,
            CogScriptLanguageConstants.ScriptCSharp);
        if (block.ScriptError != null && block.ScriptError.Contains("Compile Error"))
        {
            throw new InvalidOperationException(block.ScriptError);
        }
    }

    private static void Save(CogToolBlock block, string file)
    {
        CogSerializer.SaveObjectToFile(block, file);
        Console.WriteLine(file + "  " + new FileInfo(file).Length + " bytes");
        block.Dispose();
    }
}
