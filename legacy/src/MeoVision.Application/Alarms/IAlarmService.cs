using System;
using System.Threading;
using System.Threading.Tasks;

namespace MeoVision.Application.Alarms;

public interface IAlarmService
{
    Task RaiseAsync(int code, string message, AlarmSeverity severity, CancellationToken ct = default);

    event EventHandler<AlarmRaisedEventArgs>? AlarmRaised;
}
