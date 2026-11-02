namespace MeoVision.Core.Contracts;

public enum VisionOutcome
{
    Accept,
    Warning,
    Reject,
    Error
}

public enum VisionFailureReason
{
    None,
    PartNotFound,
    MeasurementFailed,
    SystemError
}
