// File: MakeToolChainJobs.cs — Mẫu (không thuộc solution)
// Minh hoạ: docs/vision-tools/VISIONPRO_TOOLS.md — job NỐI NHIỀU TOOL, dựng bằng code.
//
// Dựng 3 job theo hợp đồng của MeoVision (input "InputImage", output có tên, tham số nhanh là
// terminal input kiểu số): tool + tham số ở đây, script đọc từ scripts/*.cs; train PMAlign từ
// ảnh mẫu, lưu .vpp, rồi NẠP LẠI từng file và chạy trên cả bộ ảnh, so đáp án trong truth.csv:
//   C1_measure.vpp  PMAlign → Fixture → Caliper ×2 + FindCircle ×2 → DistancePointPoint
//   C2_defects.vpp  PMAlign → Fixture → Blob (vùng đi theo tấm)
//   C3_corner.vpp   PMAlign → Fixture → FindLine ×2 → IntersectLineLine + AngleLineLine
// Biên dịch + chạy: samples\toolchains\build.cmd (cần VisionPro 9.x x64 + license).
//   MakeToolChainJobs.exe <thư mục jobs> <thư mục images> [<thư mục scripts>]
// Mã thoát 0 = mọi ảnh khớp đáp án; 1 = có ảnh lệch (bảng in ra cho biết ảnh nào, output nào).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Cognex.VisionPro;
using Cognex.VisionPro.Blob;
using Cognex.VisionPro.CalibFix;
using Cognex.VisionPro.Caliper;
using Cognex.VisionPro.Dimensioning;
using Cognex.VisionPro.ImageFile;
using Cognex.VisionPro.Implementation.Internal;
using Cognex.VisionPro.PMAlign;
using Cognex.VisionPro.ToolBlock;

internal static class MakeToolChainJobs
{
    // Hình học của tấm mẫu — khớp make_bracket_images.py. Mọi vùng của tool phía sau Fixture
    // khai trong HỆ TOẠ ĐỘ TẤM (gốc ở tâm tấm, pixel của ảnh train), không phải toạ độ ảnh.
    private const double PlateW = 360, PlateH = 180, HoleX = 115, HoleR = 24;
    private const double TrainCenterX = 320, TrainCenterY = 240;

    private static int Main(string[] args)
    {
        string jobs = args.Length > 0 ? args[0] : "jobs";
        string images = args.Length > 1 ? args[1] : "images";
        string scripts = args.Length > 2 ? args[2] : "scripts";
        Directory.CreateDirectory(jobs);
        CogImage8Grey train = LoadImage(Path.Combine(images, "ok_01.bmp"));

        CogToolBlock measure = NewBlock(train);
        measure.Tools.Add(NewCaliper("Width", 0, -80, 440, 8, 0, PlateW));
        measure.Tools.Add(NewCaliper("Height", -160, 0, 240, 8, Math.PI / 2, PlateH));
        measure.Tools.Add(NewHole("HoleLeft", -HoleX));
        measure.Tools.Add(NewHole("HoleRight", HoleX));
        measure.Tools.Add(new CogDistancePointPointTool { Name = "Pitch" });
        AddOutputs(measure, "Width", "Height", "Pitch", "HoleDiameter");
        SetScript(measure, Path.Combine(scripts, "C1_measure.cs"));
        Save(measure, Path.Combine(jobs, "C1_measure.vpp"));

        CogToolBlock defects = NewBlock(train);
        defects.Inputs.Add(new CogToolBlockTerminal("MinArea", 30));
        defects.Inputs.Add(new CogToolBlockTerminal("Threshold", 120));
        defects.Tools.Add(NewDefectBlob());
        defects.Outputs.Add(new CogToolBlockTerminal("DefectCount", typeof(int)));
        defects.Outputs.Add(new CogToolBlockTerminal("MaxDefectArea", typeof(double)));
        SetScript(defects, Path.Combine(scripts, "C2_defects.cs"));
        Save(defects, Path.Combine(jobs, "C2_defects.vpp"));

        CogToolBlock corner = NewBlock(train);
        // Cạnh trên: trái → phải. Cạnh trái: dưới → lên. Caliper tìm theo hướng +90° so với
        // chiều đoạn thẳng, nên cả hai đều nhìn VÀO trong tấm: nền tối → tấm sáng.
        corner.Tools.Add(NewEdge("TopEdge", -160, -PlateH / 2, 160, -PlateH / 2));
        corner.Tools.Add(NewEdge("LeftEdge", -PlateW / 2, 70, -PlateW / 2, -70));
        corner.Tools.Add(new CogIntersectLineLineTool { Name = "Corner" });
        corner.Tools.Add(new CogAngleLineLineTool { Name = "Squareness" });
        AddOutputs(corner, "X", "Y", "Angle", "Squareness");
        SetScript(corner, Path.Combine(scripts, "C3_corner.cs"));
        Save(corner, Path.Combine(jobs, "C3_corner.vpp"));

        string truth = Path.Combine(images, "truth.csv");
        return File.Exists(truth) ? Verify(jobs, images, truth) : 0;
    }

    // ---------- dựng tool ----------

    /// <summary>Khung mọi job: InputImage, tham số nhanh AcceptThreshold, PMAlign, Fixture.
    /// </summary>
    private static CogToolBlock NewBlock(CogImage8Grey train)
    {
        CogToolBlock block = new CogToolBlock();
        block.Inputs.Add(new CogToolBlockTerminal("InputImage", typeof(CogImage8Grey)));
        block.Inputs.Add(new CogToolBlockTerminal("AcceptThreshold", 0.6));

        // Train trên ảnh tư thế gốc: vùng ôm cả tấm + 20 px nền để có đủ cạnh ngoài. Gốc mẫu
        // đặt ở TÂM tấm → hệ Fixture có gốc ở tâm tấm, trục x dọc cạnh dài.
        CogPMAlignTool locate = new CogPMAlignTool { Name = "Locate" };
        CogRectangle region = new CogRectangle();
        region.SetCenterWidthHeight(TrainCenterX, TrainCenterY, PlateW + 40, PlateH + 40);
        locate.Pattern.TrainImage = train;
        locate.Pattern.TrainRegion = region;
        locate.Pattern.Origin.TranslationX = TrainCenterX;
        locate.Pattern.Origin.TranslationY = TrainCenterY;
        locate.Pattern.Train();
        // Tấm chỉ lệch tối đa ±15° trên băng tải: mở ±30° là đủ, mở 360° vừa chậm vừa dễ nhận
        // nhầm tấm lật 180° (rãnh khuyết là chi tiết duy nhất phân biệt hai chiều).
        locate.RunParams.ZoneAngle.Configuration = CogPMAlignZoneConstants.LowHigh;
        locate.RunParams.ZoneAngle.Low = -Math.PI / 6;
        locate.RunParams.ZoneAngle.High = Math.PI / 6;
        locate.RunParams.ApproximateNumberToFind = 1;
        block.Tools.Add(locate);

        block.Tools.Add(new CogFixtureTool { Name = "Fixture" });
        return block;
    }

    /// <summary>
    /// Caliper chế độ cặp cạnh: vùng tìm (search) dọc trục x của hình chữ nhật xoay, chiếu
    /// (projection) dọc trục y. Cạnh 0 nền tối → tấm sáng, cạnh 1 ngược lại.
    /// </summary>
    private static CogCaliperTool NewCaliper(string name, double cx, double cy,
        double searchLength, double projectionLength, double rotation, double expectedWidth)
    {
        CogCaliperTool tool = new CogCaliperTool { Name = name };
        CogRectangleAffine region = new CogRectangleAffine();
        region.SetCenterLengthsRotationSkew(cx, cy, searchLength, projectionLength, rotation,
            0);
        tool.Region = region;
        tool.RunParams.EdgeMode = CogCaliperEdgeModeConstants.Pair;
        tool.RunParams.Edge0Polarity = CogCaliperPolarityConstants.DarkToLight;
        tool.RunParams.Edge1Polarity = CogCaliperPolarityConstants.LightToDark;
        tool.RunParams.EdgePairWidth = expectedWidth;
        tool.RunParams.ContrastThreshold = 30;
        tool.RunParams.FilterHalfSizeInPixels = 2;
        tool.RunParams.MaxResults = 1;
        return tool;
    }

    /// <summary>FindCircle: 16 caliper toả ra từ trong lỗ (tối) ra tấm (sáng).</summary>
    private static CogFindCircleTool NewHole(string name, double cx)
    {
        CogFindCircleTool tool = new CogFindCircleTool { Name = name };
        tool.RunParams.ExpectedCircularArc.SetCenterRadiusAngleStartAngleSpan(
            cx, 0, HoleR, 0, 2 * Math.PI);
        tool.RunParams.NumCalipers = 16;
        // ±12 px quanh bán kính: tấm dài/ngắn hơn mẫu làm pose PMAlign dịch vài pixel.
        tool.RunParams.CaliperSearchLength = 24;
        tool.RunParams.CaliperProjectionLength = 6;
        tool.RunParams.CaliperSearchDirection = CogFindCircleSearchDirectionConstants.Outward;
        CogCaliper caliper = tool.RunParams.CaliperRunParams;
        caliper.Edge0Polarity = CogCaliperPolarityConstants.DarkToLight;
        caliper.ContrastThreshold = 30;
        tool.RunParams.NumToIgnore = 2;   // bỏ 2 điểm xa nhất: bụi/ba-via sát mép lỗ
        return tool;
    }

    /// <summary>FindLine: 8 caliper vuông góc với đoạn thẳng dự kiến.</summary>
    private static CogFindLineTool NewEdge(string name, double x0, double y0, double x1,
        double y1)
    {
        CogFindLineTool tool = new CogFindLineTool { Name = name };
        tool.RunParams.ExpectedLineSegment.StartX = x0;
        tool.RunParams.ExpectedLineSegment.StartY = y0;
        tool.RunParams.ExpectedLineSegment.EndX = x1;
        tool.RunParams.ExpectedLineSegment.EndY = y1;
        tool.RunParams.NumCalipers = 8;
        tool.RunParams.CaliperSearchLength = 40;   // ±20 px: tấm 380 (ng_long_08) vẫn bắt được
        tool.RunParams.CaliperProjectionLength = 10;
        tool.RunParams.CaliperSearchDirection = Math.PI / 2;
        CogCaliper caliper = tool.RunParams.CaliperRunParams;
        caliper.Edge0Polarity = CogCaliperPolarityConstants.DarkToLight;
        caliper.ContrastThreshold = 30;
        tool.RunParams.NumToIgnore = 1;
        return tool;
    }

    /// <summary>
    /// Blob đốm tối trên tấm sáng, chỉ trong vùng giữa hai lỗ (lỗ và rãnh cũng là "đốm tối"
    /// — để vùng ôm chúng thì job đếm cả lỗ). Đốm nhỏ hơn MinArea là bụi, không phải lỗi.
    /// </summary>
    private static CogBlobTool NewDefectBlob()
    {
        CogBlobTool tool = new CogBlobTool { Name = "Defects" };
        CogRectangleAffine region = new CogRectangleAffine();
        region.SetCenterLengthsRotationSkew(0, 0, 160, 136, 0, 0);
        tool.Region = region;
        CogBlobSegmentationParams segmentation = tool.RunParams.SegmentationParams;
        segmentation.Mode = CogBlobSegmentationModeConstants.HardFixedThreshold;
        segmentation.Polarity = CogBlobSegmentationPolarityConstants.DarkBlobs;
        segmentation.HardFixedThreshold = 120;
        tool.RunParams.ConnectivityMinPixels = 30;
        return tool;
    }

    private static void AddOutputs(CogToolBlock block, params string[] names)
    {
        foreach (string name in names)
        {
            block.Outputs.Add(new CogToolBlockTerminal(name, typeof(double)));
        }
    }

    /// <summary>Script Advanced C# đọc từ scripts/*.cs (cũng dán được vào editor).</summary>
    private static void SetScript(CogToolBlock block, string scriptFile)
    {
        block.CreateNewScript(CogScriptLanguageConstants.ScriptCSharp,
            CogToolBlockScriptTypeConstants.Advanced);
        ArrayList references = block.Script.References;
        // Như MakeSampleJobs.cs: tham chiếu mặc định chỉ gồm assembly của tool ĐANG có trong
        // block; khung script dùng chung nên bổ sung đủ, đường dẫn kiểu %VPRO_ROOT%.
        string[] needed =
        {
            "Cognex.VisionPro.PMAlign.dll", "Cognex.VisionPro.CalibFix.dll",
            "Cognex.VisionPro.Caliper.dll", "Cognex.VisionPro.Dimensioning.dll",
            "Cognex.VisionPro.Blob.dll",
        };
        foreach (string name in needed)
        {
            bool present = false;
            foreach (CogReferencedAssembly r in references)
            {
                if (r.Name == name) { present = true; }
            }
            if (!present)
            {
                references.Add(new CogReferencedAssembly(name,
                    @"%VPRO_ROOT%\ReferencedAssemblies",
                    CogReferencedAssemblyPathConstants.VisionPro));
            }
        }
        string source = File.ReadAllText(scriptFile);
        block.Script = new CogScriptSupport(source, references,
            CogScriptLanguageConstants.ScriptCSharp);
        if (block.ScriptError != null && block.ScriptError.Contains("Compile Error"))
        {
            throw new InvalidOperationException(scriptFile + ": " + block.ScriptError);
        }
    }

    private static void Save(CogToolBlock block, string file)
    {
        CogSerializer.SaveObjectToFile(block, file);
        Console.WriteLine(file + "  " + new FileInfo(file).Length + " bytes");
        block.Dispose();
    }

    private static CogImage8Grey LoadImage(string file)
    {
        CogImageFile reader = new CogImageFile();
        reader.Open(file, CogImageFileModeConstants.Read);
        try
        {
            return (CogImage8Grey)reader[0];
        }
        finally
        {
            reader.Close();
        }
    }

    // ---------- tự kiểm: nạp lại .vpp, chạy cả bộ ảnh, so đáp án ----------

    private static int Verify(string jobs, string images, string truthFile)
    {
        List<Dictionary<string, string>> rows = ReadCsv(truthFile);
        int failures = 0;
        foreach (string job in new[] { "C1_measure", "C2_defects", "C3_corner" })
        {
            string file = Path.Combine(jobs, job + ".vpp");
            CogToolBlock block = (CogToolBlock)CogSerializer.LoadObjectFromFile(file);
            Console.WriteLine();
            Console.WriteLine("== " + job);
            foreach (Dictionary<string, string> row in rows)
            {
                CogImage8Grey image = LoadImage(Path.Combine(images, row["file"]));
                block.Inputs["InputImage"].Value = image;
                for (int i = 0; i < block.Outputs.Count; i++)
                {
                    block.Outputs[i].Value = null;   // như CogVisionJob: không đọc nhầm số cũ
                }
                block.Run();
                List<string> problems = Check(job, block, row);
                string values = string.Empty;
                for (int i = 0; i < block.Outputs.Count; i++)
                {
                    object v = block.Outputs[i].Value;
                    values += " " + block.Outputs[i].Name + "=" + (v == null ? "-" :
                        Convert.ToDouble(v).ToString("0.###", CultureInfo.InvariantCulture));
                }
                Console.WriteLine("{0,-18} {1,-7} {2,7:0.0} ms {3}{4}", row["file"],
                    block.RunStatus.Result, block.RunStatus.TotalTime, values,
                    problems.Count == 0 ? "  ok"
                        : "  MISMATCH: " + string.Join("; ", problems));
                if (block.RunStatus.Result != CogToolResultConstants.Accept)
                {
                    Console.WriteLine("{0,-18} message: {1}", string.Empty,
                        block.RunStatus.Message);
                }
                failures += problems.Count == 0 ? 0 : 1;
                block.Inputs["InputImage"].Value = null;
            }
            block.Dispose();
        }
        Console.WriteLine();
        Console.WriteLine(failures == 0 ? "ALL MATCH" : failures + " image(s) MISMATCH");
        return failures == 0 ? 0 : 1;
    }

    /// <summary>Đáp án mong đợi của từng job trên một ảnh; trả danh sách chỗ lệch.</summary>
    private static List<string> Check(string job, CogToolBlock block,
        Dictionary<string, string> row)
    {
        List<string> problems = new List<string>();
        CogToolResultConstants status = block.RunStatus.Result;
        if (job == "C1_measure")
        {
            if (row["pitch"].Length == 0)
            {
                // Thiếu lỗ: job phải Reject (không phải Error, không phải Accept với số rác).
                Expect(status == CogToolResultConstants.Reject, "expected Reject", problems);
                return problems;
            }
            Expect(status == CogToolResultConstants.Accept, "expected Accept", problems);
            Near(block, "Width", Num(row, "width"), 1.0, problems);
            Near(block, "Height", Num(row, "height"), 1.0, problems);
            Near(block, "Pitch", Num(row, "pitch"), 1.0, problems);
            Near(block, "HoleDiameter", Num(row, "hole_diameter"), 1.0, problems);
        }
        else if (job == "C2_defects")
        {
            Expect(status == CogToolResultConstants.Accept, "expected Accept", problems);
            Near(block, "DefectCount", Num(row, "defects"), 0.0, problems);
        }
        else
        {
            Expect(status == CogToolResultConstants.Accept, "expected Accept", problems);
            Near(block, "X", Num(row, "corner_x"), 1.5, problems);
            Near(block, "Y", Num(row, "corner_y"), 1.5, problems);
            Near(block, "Angle", Num(row, "angle_deg"), 0.3, problems);
            Near(block, "Squareness", 90, 0.3, problems);
        }
        return problems;
    }

    private static void Expect(bool condition, string text, List<string> problems)
    {
        if (!condition) { problems.Add(text); }
    }

    private static void Near(CogToolBlock block, string output, double expected,
        double tolerance, List<string> problems)
    {
        object value = block.Outputs[output].Value;
        if (value == null)
        {
            problems.Add(output + " not set");
            return;
        }
        double actual = Convert.ToDouble(value, CultureInfo.InvariantCulture);
        if (Math.Abs(actual - expected) > tolerance)
        {
            problems.Add(string.Format(CultureInfo.InvariantCulture,
                "{0}={1:0.###} (want {2})", output, actual, expected));
        }
    }

    private static double Num(Dictionary<string, string> row, string key) =>
        double.Parse(row[key], CultureInfo.InvariantCulture);

    private static List<Dictionary<string, string>> ReadCsv(string file)
    {
        string[] lines = File.ReadAllLines(file);
        string[] header = lines[0].Split(',');
        List<Dictionary<string, string>> rows = new List<Dictionary<string, string>>();
        for (int i = 1; i < lines.Length; i++)
        {
            if (lines[i].Trim().Length == 0) { continue; }
            string[] cells = lines[i].Split(',');
            Dictionary<string, string> row = new Dictionary<string, string>();
            for (int c = 0; c < header.Length; c++)
            {
                row[header[c]] = c < cells.Length ? cells[c] : string.Empty;
            }
            rows.Add(row);
        }
        return rows;
    }
}
