using System;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using MeoVision.App.Alerts;
using MeoVision.Core;
using MeoVision.Core.Localization;

namespace MeoVision.UI.Display;

/// <summary>
/// Dải cảnh báo dưới thanh nút (DESIGN_LOG [28] D3): ẩn khi mọi thứ bình thường; đỏ khi có lỗi kéo dài,
/// vàng khi chỉ có cảnh báo. Không chặn gì — máy vẫn nhận câu trả lời qua kênh; bấm vào thì xem danh sách
/// và cách xử lý (D4).
/// </summary>
public sealed class AlertBar : Panel
{
    private static readonly Color ErrorBack = Color.FromArgb(0xF8, 0xD7, 0xDA);
    private static readonly Color ErrorFore = Color.FromArgb(0x84, 0x20, 0x29);
    private static readonly Color WarningBack = Color.FromArgb(0xFF, 0xF3, 0xCD);
    private static readonly Color WarningFore = Color.FromArgb(0x66, 0x4D, 0x03);

    private readonly ILocalizer _text;
    private readonly AlertCenter _alerts;
    // Nút phẳng thay cho nhãn: bấm bằng ngón tay, bằng bàn phím (Enter), và tự động hoá bấm được.
    private readonly Button _label = new()
    {
        Name = "alertBar", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
        Cursor = Cursors.Hand, FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = false,
        Font = new Font("Segoe UI Semibold", 11f), Padding = new Padding(12, 0, 0, 0),
        AutoEllipsis = true,
    };

    public AlertBar(ILocalizer text, AlertCenter alerts)
    {
        Guard.ThrowIfNull(text, nameof(text));
        Guard.ThrowIfNull(alerts, nameof(alerts));
        _text = text;
        _alerts = alerts;
        Dock = DockStyle.Top;
        Height = 38;
        Visible = false;
        _label.FlatAppearance.BorderSize = 0;
        Controls.Add(_label);
        _label.Click += (_, _) => ShowDetails();
        // Sự kiện đến từ luồng watchdog/luồng lệnh. Dải đang ẩn thì CHƯA có handle (đo thật: dải không
        // bao giờ hiện) — chuyển về luồng giao diện qua form chứa nó.
        _alerts.Changed += (_, _) =>
        {
            Form? form = FindForm();
            if (form is { IsHandleCreated: true })
            {
                form.BeginInvoke(new Action(RefreshBar));
            }
        };
        _text.LanguageChanged += (_, _) => RefreshBar();
    }

    /// <summary>Câu gợi ý cách xử lý của một thông điệp (khoá <c>&lt;khoá&gt;.hint</c>), hoặc rỗng.</summary>
    public static string Hint(ILocalizer text, Msg message)
    {
        Guard.ThrowIfNull(text, nameof(text));
        Guard.ThrowIfNull(message, nameof(message));
        return text.TryText(message.Key + ".hint", out string hint) ? hint : string.Empty;
    }

    /// <summary>Vẽ lại theo danh sách cảnh báo hiện có — gọi trên luồng giao diện.</summary>
    public void RefreshBar()
    {
        var active = _alerts.Active;
        Visible = active.Count > 0;
        if (active.Count == 0)
        {
            return;
        }
        Alert first = active[0];
        bool error = first.Level == AlertLevel.Error;
        BackColor = error ? ErrorBack : WarningBack;
        _label.BackColor = BackColor;
        _label.ForeColor = error ? ErrorFore : WarningFore;
        _label.Text = (error ? "⛔  " : "⚠  ") + _text.Text(first.Message) +
                      (active.Count > 1 ? "   " + _text.Text("AlertBar.more", active.Count - 1) : "") +
                      "   ›  " + _text.Text("AlertBar.details");
    }

    private void ShowDetails()
    {
        using Form form = new()
        {
            Text = _text.Text("AlertBar.title"), Font = new Font("Segoe UI", 10f),
            StartPosition = FormStartPosition.CenterParent, ShowInTaskbar = false,
        };
        ScreenFit.Apply(form, new Size(1100, 420));
        ListView list = new()
        {
            Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, ShowItemToolTips = true,
        };
        list.Columns.Add(_text.Text("AlertBar.colSince"), 130);
        list.Columns.Add(_text.Text("AlertBar.colMessage"), 470);
        list.Columns.Add(_text.Text("AlertBar.colHint"), 460);
        foreach (Alert a in _alerts.Active)
        {
            ListViewItem item = new(a.Since.ToString("dd/MM HH:mm:ss", CultureInfo.InvariantCulture))
            {
                ForeColor = a.Level == AlertLevel.Error ? ErrorFore : WarningFore,
            };
            item.SubItems.Add(_text.Text(a.Message));
            string hint = Hint(_text, a.Message);
            item.SubItems.Add(hint);
            item.ToolTipText = hint;
            list.Items.Add(item);
        }
        form.Controls.Add(list);
        form.ShowDialog(FindForm());
    }
}
