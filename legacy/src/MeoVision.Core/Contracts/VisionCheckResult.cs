namespace MeoVision.Core.Contracts;

public sealed record VisionCheckResult(
    string Name,
    bool Passed,
    double MeasuredValue,
    double? LowLimit,
    double? HighLimit);
