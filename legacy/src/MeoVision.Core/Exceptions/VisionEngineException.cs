using System;
using MeoVision.Core.Contracts;

namespace MeoVision.Core.Exceptions;

public class VisionEngineException : Exception
{
    public VisionFailureReason FailureReason { get; }

    public string? JobName { get; }

    public VisionEngineException(
        string message,
        VisionFailureReason reason = VisionFailureReason.SystemError,
        string? jobName = null)
        : base(message)
    {
        FailureReason = reason;
        JobName = jobName;
    }

    public VisionEngineException(
        string message,
        Exception innerException,
        VisionFailureReason reason = VisionFailureReason.SystemError,
        string? jobName = null)
        : base(message, innerException)
    {
        FailureReason = reason;
        JobName = jobName;
    }
}

public sealed class VisionRecipeException : VisionEngineException
{
    public VisionRecipeException(string message, string? jobName = null)
        : base(message, VisionFailureReason.SystemError, jobName)
    {
    }

    public VisionRecipeException(string message, Exception innerException, string? jobName = null)
        : base(message, innerException, VisionFailureReason.SystemError, jobName)
    {
    }
}

public sealed class VisionJobLoadException : VisionEngineException
{
    public VisionJobLoadException(string message, string? jobName = null)
        : base(message, VisionFailureReason.SystemError, jobName)
    {
    }

    public VisionJobLoadException(string message, Exception innerException, string? jobName = null)
        : base(message, innerException, VisionFailureReason.SystemError, jobName)
    {
    }
}

public sealed class VisionEngineTimeoutException : VisionEngineException
{
    public VisionEngineTimeoutException(string message, string? jobName = null)
        : base(message, VisionFailureReason.SystemError, jobName)
    {
    }
}
