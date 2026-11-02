using System;
using System.Collections.Generic;
using System.Linq;
using MeoVision.Core;

namespace MeoVision.App.Comm;

/// <summary>
/// Một cặp bản tin: máy gửi gì, vision trả gì (đúng chuỗi đã gửi), mất bao lâu, và lý do NG/LỖI — lý do
/// KHÔNG gửi máy (DESIGN_LOG [29]) nhưng người nghiệm thu cần thấy.
/// </summary>
public sealed record CommEntry(long Index, DateTime Time, string Channel, string Request,
    string Reply, string Reason, long Ms, bool Test);

/// <summary>
/// Vài trăm bản tin gần nhất của mọi kênh, trong bộ nhớ (DESIGN_LOG [28] D2). Nghiệm thu với PLC mà
/// không thấy chuỗi thô đi/về thì chỉ đoán — RefVis-C, D, H đều có màn xem gửi/nhận. Bản ghi lâu dài
/// vẫn là log ứng dụng.
/// </summary>
public sealed class CommLog
{
    public const int Capacity = 500;

    private readonly object _sync = new();
    private readonly Queue<CommEntry> _entries = new();
    private long _next;

    public void Add(DateTime time, string channel, string request, string reply, string reason,
        long ms, bool test)
    {
        Guard.ThrowIfNull(channel, nameof(channel));
        Guard.ThrowIfNull(request, nameof(request));
        Guard.ThrowIfNull(reply, nameof(reply));
        Guard.ThrowIfNull(reason, nameof(reason));
        lock (_sync)
        {
            _entries.Enqueue(new CommEntry(++_next, time, channel, request, reply, reason, ms, test));
            while (_entries.Count > Capacity)
            {
                _entries.Dequeue();
            }
        }
    }

    /// <summary>Các bản tin có số thứ tự lớn hơn <paramref name="afterIndex"/> (0 = tất cả còn giữ).</summary>
    public IReadOnlyList<CommEntry> Since(long afterIndex)
    {
        lock (_sync)
        {
            return _entries.Where(e => e.Index > afterIndex).ToList();
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            _entries.Clear();
        }
    }
}
