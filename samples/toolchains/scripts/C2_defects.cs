// Script ToolBlock — Advanced, C# 5 (script compiler của VisionPro 9.0 CR2).
// C2 — lỗi bề mặt: PMAlign → Fixture → Blob (vùng đi theo tấm)
// MakeToolChainJobs.cs đọc file này khi dựng C2_defects.vpp (nguồn duy nhất). Dựng tay trong
// QuickBuild: dán nguyên file vào ToolBlock → Edit Script (Advanced); tên tool phải khớp.
using System;
using Cognex.VisionPro;
using Cognex.VisionPro.ToolBlock;
using Cognex.VisionPro.PMAlign;
using Cognex.VisionPro.CalibFix;
using Cognex.VisionPro.Blob;
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

  public override bool GroupRun(ref string message, ref CogToolResultConstants result)
  {
    CogImage8Grey part = Locate(ref message, ref result);
    if (part == null) { return false; }
    CogBlobTool defects = (CogBlobTool)mToolBlock.Tools["Defects"];
    defects.RunParams.ConnectivityMinPixels = (int)mToolBlock.Inputs["MinArea"].Value;
    defects.RunParams.SegmentationParams.HardFixedThreshold =
      (int)mToolBlock.Inputs["Threshold"].Value;
    defects.InputImage = part;
    if (!Step(defects, ref message, ref result)) { return false; }

    int count = 0;
    double maxArea = 0;
    if (defects.Results != null)
    {
      CogBlobResultCollection blobs = defects.Results.GetBlobs();
      count = blobs.Count;
      foreach (CogBlobResult blob in blobs)
      {
        if (blob.Area > maxArea) { maxArea = blob.Area; }
      }
    }
    mToolBlock.Outputs["DefectCount"].Value = count;
    mToolBlock.Outputs["MaxDefectArea"].Value = maxArea;
    message = string.Empty;
    result = CogToolResultConstants.Accept;
    return false;
  }
}
