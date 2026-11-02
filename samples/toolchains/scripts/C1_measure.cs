// Script ToolBlock — Advanced, C# 5 (script compiler của VisionPro 9.0 CR2).
// C1 — đo kích thước: PMAlign → Fixture → Caliper ×2 + FindCircle ×2 → DistancePointPoint
// MakeToolChainJobs.cs đọc file này khi dựng C1_measure.vpp (nguồn duy nhất). Dựng tay trong
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

  private static CogCircle Circle(CogFindCircleTool tool)
  {
    try { return tool.Results == null ? null : tool.Results.GetCircle(); }
    catch (Exception) { return null; }
  }

  public override bool GroupRun(ref string message, ref CogToolResultConstants result)
  {
    CogImage8Grey part = Locate(ref message, ref result);
    if (part == null) { return false; }
    CogCaliperTool width = (CogCaliperTool)mToolBlock.Tools["Width"];
    CogCaliperTool height = (CogCaliperTool)mToolBlock.Tools["Height"];
    CogFindCircleTool holeLeft = (CogFindCircleTool)mToolBlock.Tools["HoleLeft"];
    CogFindCircleTool holeRight = (CogFindCircleTool)mToolBlock.Tools["HoleRight"];
    CogDistancePointPointTool pitch = (CogDistancePointPointTool)mToolBlock.Tools["Pitch"];

    width.InputImage = part;
    height.InputImage = part;
    if (!Step(width, ref message, ref result)) { return false; }
    if (!Step(height, ref message, ref result)) { return false; }
    if (width.Results == null || width.Results.Count == 0)
    { return Reject("Width: edge pair not found", ref message, ref result); }
    if (height.Results == null || height.Results.Count == 0)
    { return Reject("Height: edge pair not found", ref message, ref result); }

    holeLeft.InputImage = part;
    holeRight.InputImage = part;
    if (!Step(holeLeft, ref message, ref result)) { return false; }
    if (!Step(holeRight, ref message, ref result)) { return false; }
    CogCircle left = Circle(holeLeft);
    CogCircle right = Circle(holeRight);
    if (left == null)
    { return Reject("HoleLeft: hole not found", ref message, ref result); }
    if (right == null)
    { return Reject("HoleRight: hole not found", ref message, ref result); }

    // Tool hình học nhận KẾT QUẢ của tool trước (tâm hai lỗ) — không cần ảnh để tính, ảnh
    // vào chỉ để biết hệ toạ độ và vẽ đồ hoạ lên đúng chỗ.
    pitch.InputImage = part;
    pitch.StartX = left.CenterX;
    pitch.StartY = left.CenterY;
    pitch.EndX = right.CenterX;
    pitch.EndY = right.CenterY;
    if (!Step(pitch, ref message, ref result)) { return false; }

    mToolBlock.Outputs["Width"].Value = width.Results[0].Width;
    mToolBlock.Outputs["Height"].Value = height.Results[0].Width;
    mToolBlock.Outputs["Pitch"].Value = pitch.Distance;
    mToolBlock.Outputs["HoleDiameter"].Value = left.Radius + right.Radius;
    message = string.Empty;
    result = CogToolResultConstants.Accept;
    return false;
  }
}
