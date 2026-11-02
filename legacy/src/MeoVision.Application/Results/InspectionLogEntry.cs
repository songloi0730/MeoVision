using System;
using MeoVision.Core.Contracts;

namespace MeoVision.Application.Results;

public sealed record InspectionLogEntry(
    Guid CorrelationId,
    string JobName,
    VisionOutcome Outcome,
    VisionFailureReason FailureReason,
    double Score,
    DateTimeOffset TimestampUtc,
    int AttemptCount,
    string? SavedImagePath,
    double ProcessingTimeMs);
