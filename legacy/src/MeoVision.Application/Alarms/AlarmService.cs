using System;
using System.Threading;
using System.Threading.Tasks;
using MeoVision.Core;
using Microsoft.Extensions.Logging;

namespace MeoVision.Application.Alarms;

public sealed class AlarmService : IAlarmService
{
    private readonly ILogger<AlarmService> _logger;

    public AlarmService(ILogger<AlarmService> logger)
    {
        Guard.ThrowIfNull(logger, nameof(logger));
        _logger = logger;
    }

    public event EventHandler<AlarmRaisedEventArgs>? AlarmRaised;

    public Task RaiseAsync(int code, string message, AlarmSeverity severity, CancellationToken ct = default)
    {
        Guard.ThrowIfNull(message, nameof(message));
        ct.ThrowIfCancellationRequested();

        AlarmRaisedEventArgs args = new(code, message, severity, DateTimeOffset.UtcNow);

        LogLevel level = severity switch
        {
            AlarmSeverity.Info => LogLevel.Information,
            AlarmSeverity.Warning => LogLevel.Warning,
            AlarmSeverity.Stop => LogLevel.Error,
            _ => LogLevel.Information
        };
        _logger.Log(level, "Alarm {Code} [{Severity}]: {Message}", code, severity, message);

        AlarmRaised?.Invoke(this, args);
        return Task.CompletedTask;
    }
}
