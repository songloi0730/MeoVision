using System;
using MeoVision.Core;

namespace MeoVision.Application.Alarms;

public sealed class AlarmRaisedEventArgs : EventArgs
{
    public int Code { get; }

    public string Message { get; }

    public AlarmSeverity Severity { get; }

    public DateTimeOffset TimestampUtc { get; }

    public AlarmRaisedEventArgs(int code, string message, AlarmSeverity severity, DateTimeOffset timestampUtc)
    {
        Guard.ThrowIfNull(message, nameof(message));
        Code = code;
        Message = message;
        Severity = severity;
        TimestampUtc = timestampUtc;
    }
}
