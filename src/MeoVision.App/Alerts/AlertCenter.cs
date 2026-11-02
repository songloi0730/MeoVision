using System;
using System.Collections.Generic;
using System.Linq;
using MeoVision.Core;
using MeoVision.Core.Localization;

namespace MeoVision.App.Alerts;

public enum AlertLevel
{
    Warning,
    Error,
}

/// <summary>
/// Một tình trạng ĐANG kéo dài cần người biết. <see cref="Id"/> cố định cho cùng một nguồn (vd
/// <c>device.TopCam</c>) để nâng lên/xoá đúng cảnh báo.
/// </summary>
public sealed record Alert(string Id, AlertLevel Level, Msg Message, DateTime Since);

/// <summary>
/// Dải cảnh báo không chặn (DESIGN_LOG [28] D3). Các chương trình tham khảo báo lỗi kéo dài bằng hộp
/// thoại modal — chặn chu trình, không tới được máy, ca đêm chồng hàng chục hộp. Ở đây chỉ giữ danh sách
/// tình trạng đang có; màn chính vẽ một dải vàng/đỏ, bấm vào xem chi tiết và cách xử lý (D4). Chỉ những
/// gì KÉO DÀI và cần người làm gì đó mới vào đây — không phải mọi dòng log (ISA-18.2: cảnh báo vô ích
/// làm người ta thôi nhìn).
/// </summary>
public sealed class AlertCenter
{
    private readonly object _sync = new();
    private readonly Dictionary<string, Alert> _alerts = new(StringComparer.Ordinal);
    private readonly Func<DateTime> _now;

    public AlertCenter(Func<DateTime>? now = null) => _now = now ?? (() => DateTime.Now);

    public event EventHandler? Changed;

    /// <summary>Thêm hoặc cập nhật; cùng id thì giữ thời điểm bắt đầu.</summary>
    public void Raise(string id, AlertLevel level, Msg message)
    {
        Guard.ThrowIfNull(id, nameof(id));
        Guard.ThrowIfNull(message, nameof(message));
        bool changed;
        lock (_sync)
        {
            DateTime since = _alerts.TryGetValue(id, out Alert? old) ? old.Since : _now();
            Alert next = new(id, level, message, since);
            changed = old is null || old.Level != level || !Equals(old.Message, message);
            _alerts[id] = next;
        }
        if (changed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Clear(string id)
    {
        Guard.ThrowIfNull(id, nameof(id));
        bool removed;
        lock (_sync)
        {
            removed = _alerts.Remove(id);
        }
        if (removed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Xoá mọi cảnh báo có id thoả <paramref name="predicate"/> (vd mọi <c>bypass.*</c>).</summary>
    public void ClearWhere(Func<string, bool> predicate)
    {
        Guard.ThrowIfNull(predicate, nameof(predicate));
        int removed;
        lock (_sync)
        {
            List<string> ids = _alerts.Keys.Where(predicate).ToList();
            ids.ForEach(id => _alerts.Remove(id));
            removed = ids.Count;
        }
        if (removed > 0)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Lỗi trước cảnh báo; trong cùng mức, cái có trước lên trước.</summary>
    public IReadOnlyList<Alert> Active
    {
        get
        {
            lock (_sync)
            {
                return _alerts.Values.OrderByDescending(a => a.Level).ThenBy(a => a.Since)
                    .ThenBy(a => a.Id, StringComparer.Ordinal).ToList();
            }
        }
    }
}
