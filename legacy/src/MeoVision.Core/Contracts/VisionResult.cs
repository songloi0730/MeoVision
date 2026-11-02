using System;
using System.Collections.Generic;
using System.Drawing;

namespace MeoVision.Core.Contracts;

public sealed record VisionResult(
    Guid CorrelationId,
    string JobName,
    VisionOutcome Outcome,
    VisionFailureReason FailureReason,
    double Score,
    double X,
    double Y,
    double AngleDeg,
    string? Code,
    IReadOnlyList<VisionCheckResult> Checks,
    DateTimeOffset TimestampUtc,
    double ProcessingTimeMs,
    // Engine cấp 1 Bitmap mới cho mỗi chu trình; caller (InspectionCycleService) sở hữu việc
    // Dispose sau khi UI đã dùng xong (clone nếu cần giữ lâu hơn) và INgImageArchiver đã ghi đĩa
    // xong (nếu có) — không tuân thủ đúng thứ tự này là nguồn lỗi leak Bitmap kinh điển của WinForms.
    Bitmap? Image)
{
    public bool Pass => Outcome is VisionOutcome.Accept or VisionOutcome.Warning;
}
