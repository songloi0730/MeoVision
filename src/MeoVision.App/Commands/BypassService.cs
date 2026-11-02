using System;
using System.Collections.Generic;
using System.Linq;
using MeoVision.Core;

namespace MeoVision.App.Commands;

/// <summary>Một lệnh đang được bỏ qua: ai, vì sao, tới khi nào, đã bỏ qua bao nhiêu lần.</summary>
public sealed class BypassEntry
{
    public BypassEntry(string code, string reason, string user, DateTime since, DateTime until)
    {
        Code = code;
        Reason = reason;
        User = user;
        Since = since;
        Until = until;
    }

    public string Code { get; }

    public string Reason { get; }

    public string User { get; }

    public DateTime Since { get; }

    public DateTime Until { get; }

    /// <summary>Số lệnh đã trả BYPASS — số sản phẩm KHÔNG được vision kiểm.</summary>
    public int Count { get; internal set; }
}

/// <summary>Lệnh hết hạn bỏ qua (tự động) — để ghi nhật ký.</summary>
public sealed class BypassEventArgs : EventArgs
{
    public BypassEventArgs(BypassEntry entry) => Entry = entry;

    public BypassEntry Entry { get; }
}

/// <summary>
/// Bỏ qua vision có kiểm soát (DESIGN_LOG [28] D1). Mọi máy thật đều cần chạy tiếp khi camera hỏng
/// lúc nửa đêm; các chương trình tham khảo làm bằng một cờ IM LẶNG (RefVis-D: tắt vision thì trả OK).
/// Ở đây bỏ qua là một CHẾ ĐỘ: trả trạng thái riêng (không phải OK), có lý do, có người bật, có hạn,
/// và chỉ sống trong bộ nhớ — khởi động lại MeoVision là hết bỏ qua.
/// </summary>
public sealed class BypassService
{
    /// <summary>Lâu nhất một lần bật — quá một ca thì người ca sau phải tự bật lại, có tên mình.</summary>
    public static readonly TimeSpan MaxDuration = TimeSpan.FromHours(12);

    /// <summary>Lý do ngắn hơn chừng này thì không nhận — "abc" không giải thích được cho ai.</summary>
    public const int MinReasonLength = 5;

    private readonly object _sync = new();
    private readonly Dictionary<string, BypassEntry> _active = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<DateTime> _now;

    public BypassService(Func<DateTime>? now = null) => _now = now ?? (() => DateTime.Now);

    /// <summary>Danh sách lệnh đang bỏ qua đổi (bật, tắt, hết hạn).</summary>
    public event EventHandler? Changed;

    /// <summary>Một lệnh tự hết hạn bỏ qua.</summary>
    public event EventHandler<BypassEventArgs>? Expired;

    public BypassEntry Enable(string code, TimeSpan duration, string reason, string user)
    {
        Guard.ThrowIfNull(code, nameof(code));
        Guard.ThrowIfNull(reason, nameof(reason));
        Guard.ThrowIfNull(user, nameof(user));
        if (duration <= TimeSpan.Zero || duration > MaxDuration)
        {
            throw new ArgumentOutOfRangeException(nameof(duration), duration, null);
        }
        if (reason.Trim().Length < MinReasonLength)
        {
            throw new ArgumentException("Reason too short", nameof(reason));
        }
        DateTime now = _now();
        BypassEntry entry = new(code, reason.Trim(), user, now, now + duration);
        lock (_sync)
        {
            _active[code] = entry;
        }
        Changed?.Invoke(this, EventArgs.Empty);
        return entry;
    }

    public bool Disable(string code)
    {
        Guard.ThrowIfNull(code, nameof(code));
        bool removed;
        lock (_sync)
        {
            removed = _active.Remove(code);
        }
        if (removed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
        return removed;
    }

    /// <summary>
    /// Lệnh <paramref name="code"/> đang bỏ qua thì đếm thêm một lần và trả true. Gọi từ bộ máy chạy
    /// lệnh trước khi đụng tới đèn/camera/job.
    /// </summary>
    public bool TryUse(string code, out BypassEntry? entry)
    {
        Guard.ThrowIfNull(code, nameof(code));
        ExpireOld();
        lock (_sync)
        {
            if (_active.TryGetValue(code, out entry))
            {
                entry.Count++;
                return true;
            }
        }
        return false;
    }

    /// <summary>Các lệnh đang bỏ qua (đã loại lệnh hết hạn).</summary>
    public IReadOnlyList<BypassEntry> Active
    {
        get
        {
            ExpireOld();
            lock (_sync)
            {
                return _active.Values.OrderBy(e => e.Code, StringComparer.OrdinalIgnoreCase).ToList();
            }
        }
    }

    /// <summary>Loại lệnh hết hạn; gọi định kỳ (màn chính mỗi giây) để hết hạn cả khi không có lệnh.</summary>
    public void ExpireOld()
    {
        DateTime now = _now();
        List<BypassEntry> expired;
        lock (_sync)
        {
            expired = _active.Values.Where(e => e.Until <= now).ToList();
            foreach (BypassEntry e in expired)
            {
                _active.Remove(e.Code);
            }
        }
        foreach (BypassEntry e in expired)
        {
            Expired?.Invoke(this, new BypassEventArgs(e));
        }
        if (expired.Count > 0)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
