// Script ToolBlock — Advanced, C# 5 (script compiler của VisionPro 9.0 CR2).
// C3 — góc tấm cho robot: PMAlign → Fixture → FindLine ×2 → IntersectLineLine + AngleLineLine
// MakeToolChainJobs.cs đọc file này khi dựng C3_corner.vpp (nguồn duy nhất). Dựng tay trong
// QuickBuild: dán nguyên file vào ToolBlock → Edit Script (Advanced); tên tool phải khớp.
using System;
using Cognex.VisionPro;
using Cognex.VisionPro.ToolBlock;
using Cognex.VisionPro.PMAlign;
using Cognex.VisionPro.CalibFix;
using Cognex.VisionPro.Caliper;
using Cognex.VisionPro.Dimensioning;
public class CogToolBlockAdvancedScript : CogToolBlockAdvancedScriptBase
{
  private CogToolBlock mToolBlock;
  // Tư thế tấm của LẦN CHẠY NÀY: hệ tấm → hệ đang chọn của ảnh vào (pixel, hoặc mm nếu ảnh
  // tới đã có hiệu chuẩn). Dùng để đổi kết quả đo trong hệ tấm ngược ra toạ độ ảnh/máy.
  private CogTransform2DLinear mPose;

  public override void Initialize(Cognex.VisionPro.ToolGroup.CogToolGroup host)
  { base.Initialize(host); mToolBlock = (CogToolBlock)host; }
  public override void ModifyCurrentRunRecord(ICogRecord currentRecord) { }
  public override void ModifyLastRunRecord(ICogRecord lastRecord) { }

  // Chạy một tool; tool báo Error thì dừng cả job với tên tool trong thông báo.
  private bool Step(ICogTool tool, ref string message, ref CogToolResultConstants result)
  {
    mToolBlock.RunTool(tool, ref message, ref result);
    if (tool.RunStatus.Result == CogToolResultConstants.Error)
    {
      message = tool.Name + ": " + tool.RunStatus.Message;
      result = CogToolResultConstants.Error;
      return false;
    }
    return true;
  }

  // Tool chạy được nhưng không ra kết quả (không thấy cạnh, không đủ điểm) = sản phẩm NG.
  private static bool Reject(string why, ref string message, ref CogToolResultConstants result)
  {
    message = why;
    result = CogToolResultConstants.Reject;
    return false;
  }

  // Bước chung của cả ba job: PMAlign tìm tấm → Fixture gắn hệ toạ độ tấm vào ảnh. Mọi tool
  // sau đó nhận ảnh RA của Fixture nên vùng của chúng đi theo tấm dù tấm xoay/dịch.
  private CogImage8Grey Locate(ref string message, ref CogToolResultConstants result)
  {
    CogImage8Grey image = mToolBlock.Inputs["InputImage"].Value as CogImage8Grey;
    if (image == null)
    {
      Reject("InputImage: 8-bit grey image required", ref message, ref result);
      return null;
    }
    CogPMAlignTool locate = (CogPMAlignTool)mToolBlock.Tools["Locate"];
    CogFixtureTool fixture = (CogFixtureTool)mToolBlock.Tools["Fixture"];
    locate.InputImage = image;
    locate.RunParams.AcceptThreshold = (double)mToolBlock.Inputs["AcceptThreshold"].Value;
    if (!Step(locate, ref message, ref result)) { return null; }
    if (locate.Results == null || locate.Results.Count == 0)
    {
      Reject("Locate: part not found", ref message, ref result);
      return null;
    }
    mPose = locate.Results[0].GetPose();
    fixture.InputImage = image;
    fixture.RunParams.UnfixturedFromFixturedTransform = mPose;
    if (!Step(fixture, ref message, ref result)) { return null; }
    return (CogImage8Grey)fixture.OutputImage;
  }

  private static CogLine Line(CogFindLineTool tool)
  {
    try { return tool.Results == null ? null : tool.Results.GetLine(); }
    catch (Exception) { return null; }
  }

  private static double Degrees(double radians)
  {
    return radians * 180.0 / Math.PI;
  }

  public override bool GroupRun(ref string message, ref CogToolResultConstants result)
  {
    CogImage8Grey part = Locate(ref message, ref result);
    if (part == null) { return false; }
    CogFindLineTool top = (CogFindLineTool)mToolBlock.Tools["TopEdge"];
    CogFindLineTool side = (CogFindLineTool)mToolBlock.Tools["LeftEdge"];
    CogIntersectLineLineTool corner = (CogIntersectLineLineTool)mToolBlock.Tools["Corner"];
    CogAngleLineLineTool square = (CogAngleLineLineTool)mToolBlock.Tools["Squareness"];

    top.InputImage = part;
    side.InputImage = part;
    if (!Step(top, ref message, ref result)) { return false; }
    if (!Step(side, ref message, ref result)) { return false; }
    CogLine topLine = Line(top);
    CogLine sideLine = Line(side);
    if (topLine == null)
    { return Reject("TopEdge: line not found", ref message, ref result); }
    if (sideLine == null)
    { return Reject("LeftEdge: line not found", ref message, ref result); }

    corner.InputImage = part;
    corner.LineA = topLine;
    corner.LineB = sideLine;
    if (!Step(corner, ref message, ref result)) { return false; }
    if (!corner.Intersects)
    { return Reject("Corner: edges do not intersect", ref message, ref result); }
    square.InputImage = part;
    square.LineA = topLine;
    square.LineB = sideLine;
    if (!Step(square, ref message, ref result)) { return false; }

    // Góc tấm đo trong hệ tấm → đổi ra hệ của ảnh vào bằng chính tư thế PMAlign (= phép biến
    // đổi Fixture đã dùng). Ảnh vào có hiệu chuẩn thì X/Y ra mm của máy.
    double x, y;
    mPose.MapPoint(corner.X, corner.Y, out x, out y);
    // Góc xoay: PMAlign cho góc THÔ; cạnh trên tìm bằng 8 caliper cho góc TINH trong hệ tấm.
    double edge = topLine.Rotation;
    while (edge > Math.PI / 2) { edge -= Math.PI; }
    while (edge <= -Math.PI / 2) { edge += Math.PI; }
    double squareness = Math.Abs(Degrees(square.Angle));
    if (squareness > 90) { squareness = 180 - squareness; }

    mToolBlock.Outputs["X"].Value = x;
    mToolBlock.Outputs["Y"].Value = y;
    mToolBlock.Outputs["Angle"].Value = Degrees(mPose.Rotation + edge);
    mToolBlock.Outputs["Squareness"].Value = squareness;
    message = string.Empty;
    result = CogToolResultConstants.Accept;
    return false;
  }
}
