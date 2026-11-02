using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Cognex.VisionPro;
using MeoVision.Core;
using MeoVision.Core.Commands;
using MeoVision.Core.Localization;
using MeoVision.Core.Products;

namespace MeoVision.UI.Display;

/// <summary>
/// Một khung của màn chính = một lệnh, hoặc vài lệnh khai cùng <c>display.slot</c> (Chương 22, mục
/// 22.3): đầu khung (lệnh, camera, ms, thẻ
/// OK/NG), ảnh kèm đồ hoạ kết quả, lý do NG, dải 16 kết quả gần nhất. Kết quả đến từ luồng lệnh qua
/// <see cref="Post"/>; khung chỉ vẽ kết quả MỚI NHẤT — máy chạy nhanh hơn mắt nhìn thì bỏ bớt lần
/// vẽ, không bỏ dòng nào trong dải kết quả.
/// </summary>
public sealed class ResultFrameView : UserControl
{
    /// <summary>Số kết quả trên dải; chỉ <see cref="FramesKept"/> cái mới nhất còn ảnh.</summary>
    public const int StripLength = 16;

    private readonly ILocalizer _text;
    private readonly Dictionary<string, CommandConfig> _commands =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Label _title = new();
    private readonly Label _time = new();
    private readonly Label _verdict = new();
    private readonly Button _setup = new();
    private readonly CogRecordsDisplay _display = new();
    private readonly Label _detail = new();
    private readonly FlowLayoutPanel _strip = new();
    private readonly LinkedList<CommandResult> _recent = new();
    private readonly object _sync = new();
    private readonly List<CommandResult> _pending = new();
    private CommandResult? _shown;
    private bool _flushQueued;

    public ResultFrameView(ILocalizer text, IReadOnlyList<CommandConfig> commands, int framesKept)
    {
        Guard.ThrowIfNull(text, nameof(text));
        Guard.ThrowIfNull(commands, nameof(commands));
        if (commands.Count == 0)
        {
            throw new ArgumentException("A frame needs at least one command", nameof(commands));
        }
        _text = text;
        foreach (CommandConfig c in commands)
        {
            _commands[c.Code] = c;
        }
        Codes = commands.Select(c => c.Code).ToList();
        FramesKept = framesKept;
        BackColor = Color.FromArgb(225, 225, 225);
        Margin = new Padding(3);

        Panel head = new() { Dock = DockStyle.Top, Height = 40, Padding = new Padding(6, 0, 4, 0) };
        _title.Dock = DockStyle.Fill;
        _title.ForeColor = Color.Black;
        _title.TextAlign = ContentAlignment.MiddleLeft;
        _title.Font = new Font("Segoe UI Semibold", 11f);
        CommandConfig first = commands[0];
        _title.Text = commands.Count == 1
            ? $"{first.Code} · {first.Name} · {first.Camera}"
            : string.Join(" + ", Codes) + " · " +
              string.Join(", ", commands.Select(c => c.Camera).Distinct());
        _time.Dock = DockStyle.Right;
        _time.Width = 70;
        _time.ForeColor = Color.DimGray;
        _time.TextAlign = ContentAlignment.MiddleRight;
        _verdict.Dock = DockStyle.Right;
        _verdict.Width = 64;
        _verdict.Font = new Font("Segoe UI", 12f, FontStyle.Bold);
        _verdict.TextAlign = ContentAlignment.MiddleCenter;
        _verdict.ForeColor = Color.White;
        _setup.Dock = DockStyle.Right;
        _setup.Width = 44;
        _setup.Text = "⚙";
        _setup.Font = new Font("Segoe UI Symbol", 13f);
        _setup.FlatStyle = FlatStyle.Flat;
        _setup.ForeColor = Color.Black;
        _setup.Click += (_, _) => SetupRequested?.Invoke(this, EventArgs.Empty);
        head.Controls.Add(_title);
        head.Controls.Add(_time);
        head.Controls.Add(_verdict);
        head.Controls.Add(_setup);

        _detail.Dock = DockStyle.Bottom;
        _detail.Height = 26;
        _detail.ForeColor = Color.White;
        _detail.TextAlign = ContentAlignment.MiddleLeft;
        _detail.Padding = new Padding(6, 0, 0, 0);
        _strip.Dock = DockStyle.Bottom;
        _strip.Height = 26;
        _strip.Padding = new Padding(4, 3, 0, 0);
        _strip.WrapContents = false;

        // Màn của người vận hành: không combobox record, không thanh cuộn (spike v2.1).
        _display.Dock = DockStyle.Fill;
        _display.Font = DefaultFont;   // không để control Cognex thừa hưởng font lớn (spike v2.1)
        Controls.Add(_display);
        Controls.Add(_detail);
        Controls.Add(_strip);
        Controls.Add(head);
        HandleCreated += (_, _) =>
        {
            _display.ShowRecordsDropDown = false;
            _display.Display.HorizontalScrollBar = false;
            _display.Display.VerticalScrollBar = false;
            RecordView.UseLightBackground(_display.Display);
            lock (_sync)
            {
                if (_pending.Count > 0)
                {
                    BeginInvoke(new Action(Flush));   // kết quả đến trước khi khung kịp hiện
                }
            }
        };
        ShowVerdict(null);
    }

    /// <summary>Các lệnh hiện trên khung này.</summary>
    public IReadOnlyList<string> Codes { get; }

    /// <summary>Lệnh của kết quả đang hiện (chưa có kết quả: lệnh đầu tiên) — ⚙ mở lệnh này.</summary>
    public string Code => _shown?.Code ?? Codes[0];

    /// <summary>Số kết quả gần nhất còn giữ ảnh (bấm trên dải để xem lại).</summary>
    public int FramesKept { get; }

    /// <summary>Bấm ⚙: mở Thiết lập lệnh cho lệnh này với ảnh đang hiện.</summary>
    public event EventHandler? SetupRequested;

    /// <summary>Kết quả đang hiện, kèm khung — người nhận phải Share nếu muốn giữ.</summary>
    public CommandResult? Shown => _shown;

    /// <summary>
    /// Gọi từ luồng lệnh, TRONG sự kiện <c>Completed</c>: khung đã được Share bởi người gọi và từ
    /// đây thuộc về khung hiển thị.
    /// </summary>
    public void Post(CommandResult result)
    {
        Guard.ThrowIfNull(result, nameof(result));
        bool queue;
        lock (_sync)
        {
            if (IsDisposed)
            {
                result.Frame?.Dispose();
                return;
            }
            _pending.Add(result);
            queue = !_flushQueued;
            _flushQueued = true;
        }
        if (queue && IsHandleCreated)
        {
            BeginInvoke(new Action(Flush));
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            RecordView.Clear(_display);
            lock (_sync)
            {
                foreach (CommandResult r in _pending.Concat(_recent))
                {
                    r.Frame?.Dispose();
                }
                _shown?.Frame?.Dispose();   // Dispose hai lần cùng một bản không sao
                _pending.Clear();
                _recent.Clear();
            }
            _shown = null;
            _display.Dispose();
        }
        base.Dispose(disposing);
    }

    private void Flush()
    {
        List<CommandResult> batch;
        lock (_sync)
        {
            batch = new List<CommandResult>(_pending);
            _pending.Clear();
            _flushQueued = false;
        }
        if (batch.Count == 0 || IsDisposed)
        {
            return;
        }
        foreach (CommandResult r in batch)
        {
            _recent.AddFirst(r);
        }
        Show(batch[batch.Count - 1]);
        // Ảnh chỉ giữ cho FramesKept kết quả mới nhất; cũ hơn chỉ còn chữ trên dải.
        int index = 0;
        for (LinkedListNode<CommandResult>? node = _recent.First; node is not null;
             node = node.Next, index++)
        {
            if (index >= FramesKept && node.Value.Frame is not null &&
                !ReferenceEquals(node.Value, _shown))
            {
                node.Value.Frame.Dispose();
                node.Value = node.Value with { Frame = null };
            }
        }
        while (_recent.Count > StripLength)
        {
            if (!ReferenceEquals(_recent.Last!.Value, _shown))
            {
                _recent.Last.Value.Frame?.Dispose();
            }
            _recent.RemoveLast();
        }
        RebuildStrip();
    }

    private void Show(CommandResult result)
    {
        CommandResult? old = _shown;
        string key = _commands.TryGetValue(result.Code, out CommandConfig? c)
            ? c.Display.Result
            : string.Empty;
        RecordView.Show(_display, result.Frame, key);
        _shown = result;
        // Kết quả cũ rời khung; nếu nó đã rơi khỏi dải (hoặc mất ảnh) thì trả ảnh ngay.
        if (old is not null && !_recent.Any(r => ReferenceEquals(r, old)))
        {
            old.Frame?.Dispose();
        }
        ShowVerdict(result);
    }

    private void ShowVerdict(CommandResult? r)
    {
        _time.Text = r is null ? string.Empty : r.Times.TotalMs + " ms";
        (_verdict.Text, _verdict.BackColor) = r?.Status switch
        {
            CommandStatus.Ok => ("OK", Color.ForestGreen),
            CommandStatus.Ng => ("NG", Color.Firebrick),
            CommandStatus.Error => ("ERR", Color.DarkOrange),
            CommandStatus.Bypassed => ("BYP", Color.Goldenrod),
            _ => ("—", Color.DimGray),
        };
        _detail.Text = r is null ? string.Empty : Describe(_text, r);
        bool plain = r is null || r.Status == CommandStatus.Ok;
        _detail.BackColor = plain ? BackColor : _verdict.BackColor;
        _detail.ForeColor = plain ? Color.Black : Color.White;
    }

    /// <summary>Một dòng: các giá trị (✗ = ngoài giới hạn), hoặc lý do lỗi.</summary>
    public static string Describe(ILocalizer text, CommandResult r)
    {
        Guard.ThrowIfNull(text, nameof(text));
        Guard.ThrowIfNull(r, nameof(r));
        if (r.Error is not null && r.Outputs.Count == 0)
        {
            return text.Text(r.Error);
        }
        string values = string.Join("   ", r.Outputs.Select(o =>
            o.Name + " " + (o.Text.Length == 0 ? "—" : o.Text) + (o.InLimits ? "" : " ✗")));
        return r.Error is null ? values : values + "   " + text.Text(r.Error);
    }

    private void RebuildStrip()
    {
        _strip.SuspendLayout();
        foreach (Control c in _strip.Controls.Cast<Control>().ToList())
        {
            c.Dispose();
        }
        foreach (CommandResult r in _recent.Reverse())
        {
            Label cell = new()
            {
                Width = 18, Height = 18, Margin = new Padding(1, 0, 1, 0),
                BackColor = r.Status switch
                {
                    CommandStatus.Ok => Color.ForestGreen,
                    CommandStatus.Ng => Color.Firebrick,
                    CommandStatus.Bypassed => Color.Goldenrod,
                    _ => Color.DarkOrange,
                },
                BorderStyle = ReferenceEquals(r, _shown)
                    ? BorderStyle.FixedSingle
                    : BorderStyle.None,
                Cursor = r.Frame is null ? Cursors.Default : Cursors.Hand,
            };
            CommandResult captured = r;
            cell.Click += (_, _) =>
            {
                if (captured.Frame is not null)
                {
                    Show(captured);
                    RebuildStrip();
                }
            };
            _strip.Controls.Add(cell);
        }
        _strip.ResumeLayout();
    }
}
