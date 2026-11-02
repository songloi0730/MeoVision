using System;
using System.Drawing;

namespace MeoVision.Core.Contracts;

public sealed record VisionJobRequest(
    Guid CorrelationId,
    DateTimeOffset RequestedAtUtc,
    Bitmap? SourceImage = null)
{
    // SourceImage == null  -> engine tự thu ảnh (thật: CogAcqFifoTool; giả lập: vẽ thủ tục)
    // SourceImage != null  -> caller cấp ảnh sẵn (unit test, WorkStation.Demo replay)
    public static VisionJobRequest CreateNew(Bitmap? sourceImage = null) =>
        new(Guid.NewGuid(), DateTimeOffset.UtcNow, sourceImage);
}
