using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MeoVision.Core;

namespace MeoVision.Application.Results;

// In-memory hôm nay, có thể thay bằng bản SQLite/EF Core sau mà không đổi interface — xem
// IInspectionResultStore. Ring-evict khi vượt MaxEntries thay vì phình vô hạn trong 1 phiên chạy.
public sealed class InMemoryInspectionResultStore : IInspectionResultStore
{
    private const int MaxEntries = 500;
    private readonly object _lockObj = new();
    private readonly LinkedList<InspectionLogEntry> _entries = new();

    public Task AddAsync(InspectionLogEntry entry, CancellationToken ct = default)
    {
        Guard.ThrowIfNull(entry, nameof(entry));
        ct.ThrowIfCancellationRequested();

        lock (_lockObj)
        {
            _entries.AddFirst(entry);
            while (_entries.Count > MaxEntries)
            {
                _entries.RemoveLast();
            }
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<InspectionLogEntry>> GetRecentAsync(int maxCount, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        lock (_lockObj)
        {
            IReadOnlyList<InspectionLogEntry> snapshot = _entries.Take(maxCount).ToList();
            return Task.FromResult(snapshot);
        }
    }
}
