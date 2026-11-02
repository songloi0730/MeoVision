using System;
using System.Collections.Generic;
using System.Linq;
using MeoVision.Core;
using MeoVision.Core.Commands;

namespace MeoVision.App.Commands;

/// <summary>
/// Vài lệnh gần nhất của MỖI mã lệnh, kèm ảnh, giữ trong RAM (Chương 22, mục 22.3; DESIGN_LOG
/// [21]): nguồn ảnh để chạy thử bản nháp ngay, không phải chờ lệnh mới. Ảnh cũ hơn đọc lại từ thư
/// mục ảnh đã lưu (Chương 23) — một ảnh 5 MP là 5 MB, giữ nhiều là ăn hết RAM của máy chạy ca dài.
/// </summary>
public sealed class RecentFrames : IDisposable
{
    private readonly CommandEngine _engine;
    private readonly int _perCommand;
    private readonly object _sync = new();
    private readonly Dictionary<string, LinkedList<CommandResult>> _byCode =
        new(StringComparer.OrdinalIgnoreCase);

    /// <param name="perCommand">Số lệnh giữ cho mỗi mã; 0 = tắt.</param>
    public RecentFrames(CommandEngine engine, int perCommand = 8)
    {
        Guard.ThrowIfNull(engine, nameof(engine));
        if (perCommand < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(perCommand), perCommand, ">= 0");
        }
        _engine = engine;
        _perCommand = perCommand;
        if (perCommand > 0)
        {
            _engine.Completed += OnCompleted;
        }
    }

    /// <summary>
    /// Các lệnh gần nhất của <paramref name="code"/>, mới nhất trước. Mỗi khung là một bản
    /// <see cref="DisplayFrame.Share"/> — người gọi Dispose từng cái khi xong.
    /// </summary>
    public IReadOnlyList<CommandResult> Take(string code)
    {
        lock (_sync)
        {
            return _byCode.TryGetValue(code, out LinkedList<CommandResult>? list)
                ? list.Select(r => r with { Frame = r.Frame!.Share() }).ToList()
                : Array.Empty<CommandResult>();
        }
    }

    public void Dispose()
    {
        _engine.Completed -= OnCompleted;
        lock (_sync)
        {
            foreach (CommandResult result in _byCode.Values.SelectMany(l => l))
            {
                result.Frame!.Dispose();
            }
            _byCode.Clear();
        }
    }

    private void OnCompleted(object? sender, CommandResultEventArgs e)
    {
        if (e.Result.Frame is null)
        {
            return;   // lỗi trước khi có ảnh (đèn, chụp, quá hạn)
        }
        CommandResult kept = e.Result with { Frame = e.Result.Frame.Share() };
        lock (_sync)
        {
            if (!_byCode.TryGetValue(kept.Code, out LinkedList<CommandResult>? list))
            {
                list = new LinkedList<CommandResult>();
                _byCode[kept.Code] = list;
            }
            list.AddFirst(kept);
            while (list.Count > _perCommand)
            {
                list.Last!.Value.Frame!.Dispose();
                list.RemoveLast();
            }
        }
    }
}
