using System;
using System.Collections.Generic;

namespace MeoVision.Core.Localization;

/// <summary>
/// Thông điệp CHƯA DỊCH: một khoá ổn định + tham số (Chương 17, mục 17.6). Tầng App/Vision
/// trả về <see cref="Msg"/> thay vì chuỗi đã ghép sẵn — chỉ UI (theo ngôn ngữ người vận
/// hành) và log (theo ngôn ngữ cố định của log) mới dịch. Tham số có thể là một <see
/// cref="Msg"/> khác (dịch lồng).
/// </summary>
public sealed class Msg
{
    public static readonly Msg Empty = new(string.Empty);

    public Msg(string key, params object[] args)
    {
        Guard.ThrowIfNull(key, nameof(key));
        Key = key;
        Args = args ?? Array.Empty<object>();
    }

    public string Key { get; }

    public IReadOnlyList<object> Args { get; }

    public static Msg Of(string key, params object[] args) => new(key, args);

    /// <summary>Dạng gỡ lỗi (không dịch) — chỉ dùng khi chưa có bộ dịch.</summary>
    public override string ToString() =>
        Args.Count == 0 ? Key : Key + "(" + string.Join(", ", Args) + ")";
}
