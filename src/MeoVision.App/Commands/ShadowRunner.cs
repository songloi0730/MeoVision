using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MeoVision.App.Products;
using MeoVision.Core;
using MeoVision.Core.Commands;
using Microsoft.Extensions.Logging;

namespace MeoVision.App.Commands;

/// <summary>Bộ đếm chạy kèm: giống, khác, bỏ lượt (bận hoặc editor đang mở).</summary>
public sealed record ShadowCounters(int Same, int Different, int Skipped);

/// <summary>
/// Một lệnh mà bản nháp kết luận khác bản đang chạy: cả hai kết quả, mỗi bên một khung (cùng một
/// ảnh, record riêng). Người giữ gọi Dispose khi không cần nữa.
/// </summary>
public sealed class ShadowDifference : IDisposable
{
    public ShadowDifference(CommandResult running, CommandResult draft)
    {
        Running = running;
        Draft = draft;
    }

    public CommandResult Running { get; }

    public CommandResult Draft { get; }

    public void Dispose()
    {
        Running.Frame?.Dispose();
        Draft.Frame?.Dispose();
    }
}

/// <summary>Sự kiện một lần chạy kèm xong.</summary>
public sealed class ShadowEventArgs : EventArgs
{
    public ShadowEventArgs(CommandResult running, CommandResult draft, bool same)
    {
        Running = running;
        Draft = draft;
        Same = same;
    }

    public CommandResult Running { get; }

    public CommandResult Draft { get; }

    public bool Same { get; }
}

/// <summary>
/// Chạy kèm bản nháp trên sản phẩm thật (Chương 22, mục 22.5; DESIGN_LOG [21]). Lệnh thật trả lời
/// máy bằng bản đang chạy; SAU đó, trên luồng nền, cùng ảnh chạy thêm một lần bằng job NHÁP — đối
/// tượng riêng nên không tranh khoá với job thật (đo thật: 2 400 cặp chạy song song trên cùng ảnh,
/// 0 sai). Không giữ làn camera, không làm chậm câu trả lời. Đang bận thì bỏ lượt: chạy kèm là
/// để thống kê, không cần đủ từng lệnh.
/// </summary>
public sealed class ShadowRunner : IDisposable
{
    private readonly CommandEngine _engine;
    private readonly ILogger<ShadowRunner> _logger;
    private readonly object _sync = new();
    private readonly Queue<ShadowDifference> _differences = new();
    private DraftSession? _draft;
    private int _busy;
    private int _same;
    private int _different;
    private int _skipped;

    public ShadowRunner(CommandEngine engine, ILogger<ShadowRunner> logger)
    {
        Guard.ThrowIfNull(engine, nameof(engine));
        Guard.ThrowIfNull(logger, nameof(logger));
        _engine = engine;
        _logger = logger;
        _engine.Completed += OnCompleted;
    }

    /// <summary>Số lệnh khác kết quả giữ lại để xem (bỏ cái cũ nhất); mỗi cái một ảnh.</summary>
    public int KeepDifferences { get; set; } = 16;

    public event EventHandler<ShadowEventArgs>? Compared;

    public ShadowCounters Counters
    {
        get { lock (_sync) { return new ShadowCounters(_same, _different, _skipped); } }
    }

    /// <summary>Bản sao danh sách; các khung VẪN thuộc bộ chạy kèm — đừng Dispose.</summary>
    public IReadOnlyList<ShadowDifference> Differences
    {
        get { lock (_sync) { return _differences.ToList(); } }
    }

    /// <summary>Bắt đầu chạy kèm bản nháp; bộ đếm về 0. Null = dừng chạy kèm.</summary>
    public void Attach(DraftSession? draft)
    {
        lock (_sync)
        {
            _draft = draft;
            _same = _different = _skipped = 0;
            while (_differences.Count > 0)
            {
                _differences.Dequeue().Dispose();
            }
        }
    }

    public void Dispose()
    {
        _engine.Completed -= OnCompleted;
        Attach(null);
    }

    private void OnCompleted(object? sender, CommandResultEventArgs e)
    {
        DraftSession? draft;
        lock (_sync)
        {
            draft = _draft;
        }
        CommandResult running = e.Result;
        // Chỉ lệnh ĐÃ SỬA trong bản nháp: lệnh chưa sửa chắc chắn ra giống, chạy kèm chỉ tốn CPU
        // và làm bận lượt của lệnh đáng so.
        if (draft is null || running.Frame is null || !draft.IsChanged(running.Code))
        {
            return;
        }
        if (draft.EditorOpen || Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
        {
            lock (_sync)
            {
                _skipped++;
            }
            return;
        }
        // Giữ ảnh TRONG lúc xử lý sự kiện — sau đó bộ máy chạy lệnh trả bản của nó.
        DisplayFrame frame = running.Frame.Share();
        _ = Task.Run(() => Compare(draft, running with { Frame = frame }));
    }

    private void Compare(DraftSession draft, CommandResult running)
    {
        ShadowEventArgs? compared = null;
        bool kept = false;
        try
        {
            CommandResult? shadow = draft.Run(running.Code, running.Frame!);
            if (shadow is null)
            {
                return;   // bản nháp vừa đóng
            }
            bool same = shadow.Status == running.Status &&
                        shadow.Outputs.Select(o => o.Text).SequenceEqual(
                            running.Outputs.Select(o => o.Text));
            lock (_sync)
            {
                if (!ReferenceEquals(draft, _draft))
                {
                    shadow.Frame?.Dispose();
                    return;   // đã đổi bản nháp giữa chừng — kết quả này không thuộc bộ đếm mới
                }
                if (same)
                {
                    _same++;
                    shadow.Frame?.Dispose();
                }
                else
                {
                    _different++;
                    _differences.Enqueue(new ShadowDifference(running, shadow));
                    kept = true;
                    while (_differences.Count > Math.Max(0, KeepDifferences))
                    {
                        _differences.Dequeue().Dispose();
                    }
                }
            }
            compared = new ShadowEventArgs(running, shadow, same);
        }
        catch (Exception ex)
        {
            // Chạy kèm lỗi không được ảnh hưởng máy: ghi log, bỏ lượt.
            _logger.LogError(ex, "Shadow run of {Code} failed", running.Code);
        }
        finally
        {
            if (!kept)
            {
                running.Frame?.Dispose();
            }
            Volatile.Write(ref _busy, 0);
        }
        // Báo SAU khi đã nhận lượt mới: người nghe phản ứng bằng lệnh tiếp theo không bị bỏ lượt.
        // Khung đã trả (giống nhau) thì người nghe chỉ đọc số, không đọc ảnh.
        if (compared is not null)
        {
            Compared?.Invoke(this, compared);
        }
    }
}
