using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using MeoVision.App.Commands;
using MeoVision.App.Products;
using MeoVision.App.Security;
using MeoVision.Core;
using MeoVision.Core.Localization;
using MeoVision.Core.Products;
using MeoVision.Core.Security;

namespace MeoVision.UI.Settings;

/// <summary>
/// Cài đặt → Bỏ qua kiểm (DESIGN_LOG [28] D1). Chọn lệnh, số giờ, lý do — Kỹ sư bật, ai cũng xem. Lệnh
/// đang bỏ qua trả BYP cho máy (không phải OK), dải vàng trên màn chính, nhật ký ghi ai/vì sao; hết giờ
/// hoặc mở lại MeoVision là hết bỏ qua.
/// </summary>
public sealed class BypassForm : Form
{
    private readonly ILocalizer _text;
    private readonly BypassService _bypass;
    private readonly ProductStore _store;
    private readonly Session _session;
    private readonly ListView _list = new()
    {
        Name = "commands", Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true,
        MultiSelect = false, HideSelection = false,
    };
    private readonly NumericUpDown _hours = new()
    {
        Minimum = 0.5m, Maximum = (decimal)BypassService.MaxDuration.TotalHours, DecimalPlaces = 1,
        Increment = 0.5m, Value = 8, Width = 90,
    };
    private readonly TextBox _reason = new() { Name = "reason", Width = 520 };
    private readonly Button _enable = new() { Name = "enable", Width = 170, Height = 44 };
    private readonly Button _disable = new() { Name = "disable", Width = 170, Height = 44 };
    private readonly Label _info = new() { AutoSize = true, MaximumSize = new Size(1000, 0) };

    public BypassForm(ILocalizer text, BypassService bypass, ProductStore store, Session session)
    {
        Guard.ThrowIfNull(text, nameof(text));
        Guard.ThrowIfNull(bypass, nameof(bypass));
        Guard.ThrowIfNull(store, nameof(store));
        Guard.ThrowIfNull(session, nameof(session));
        _text = text;
        _bypass = bypass;
        _store = store;
        _session = session;
        Name = "BypassForm";
        Font = new Font("Segoe UI", 10f);
        ScreenFit.Apply(this, new Size(1100, 560));

        foreach ((string name, int width) in new[] { ("colCode", 70), ("colName", 200),
                     ("colState", 150), ("colUntil", 90), ("colCount", 80), ("colWho", 120),
                     ("colReason", 360) })
        {
            _list.Columns.Add(new ColumnHeader { Name = name, Width = width });
        }
        FlowLayoutPanel inputs = new()
        {
            Dock = DockStyle.Bottom, Height = 120, Padding = new Padding(8), FlowDirection = FlowDirection.LeftToRight,
        };
        inputs.Controls.Add(new Label { Name = "hoursLabel", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
        inputs.Controls.Add(_hours);
        inputs.Controls.Add(new Label { Name = "reasonLabel", AutoSize = true, Padding = new Padding(16, 6, 0, 0) });
        inputs.Controls.Add(_reason);
        inputs.SetFlowBreak(_reason, true);
        inputs.Controls.Add(_enable);
        inputs.Controls.Add(_disable);
        Button close = new() { Name = "close", Width = 120, Height = 44, DialogResult = DialogResult.Cancel };
        inputs.Controls.Add(close);
        inputs.SetFlowBreak(close, true);
        inputs.Controls.Add(_info);
        Label banner = new()
        {
            Name = "warning", Dock = DockStyle.Top, Height = 54, Padding = new Padding(10, 6, 10, 6),
            BackColor = Color.FromArgb(0xFF, 0xF3, 0xCD), ForeColor = Color.FromArgb(0x66, 0x4D, 0x03),
        };
        Controls.Add(_list);
        Controls.Add(banner);
        Controls.Add(inputs);
        CancelButton = close;

        _enable.Click += (_, _) => Enable();
        _disable.Click += (_, _) => Disable();
        _list.SelectedIndexChanged += (_, _) => UpdateButtons();
        _bypass.Changed += OnBypassChanged;
        FormClosed += (_, _) => _bypass.Changed -= OnBypassChanged;
        UiText.Apply(this, _text);
        Reload();
    }

    private string? SelectedCode => _list.SelectedItems.Count == 0 ? null : (string)_list.SelectedItems[0].Tag;

    private void OnBypassChanged(object? sender, EventArgs e)
    {
        if (IsHandleCreated)
        {
            BeginInvoke(new Action(Reload));
        }
    }

    private void Reload()
    {
        string? selected = SelectedCode;
        Dictionary<string, BypassEntry> active = _bypass.Active.ToDictionary(b => b.Code,
            StringComparer.OrdinalIgnoreCase);
        _list.Items.Clear();
        foreach (CommandConfig c in _store.Current?.Commands ?? new List<CommandConfig>())
        {
            ListViewItem item = new(c.Code) { Tag = c.Code };
            item.SubItems.Add(c.Name);
            if (CommandKind.IsGuide(c.Kind))
            {
                // Lệnh dẫn hướng (X, Y, góc cho chuyển động): không có gì để "bỏ qua" — DESIGN_LOG [29].
                item.SubItems.Add(_text.Text("BypassForm.stateGuide"));
                item.ForeColor = Color.Gray;
            }
            else if (active.TryGetValue(c.Code, out BypassEntry? b))
            {
                item.SubItems.Add(_text.Text("BypassForm.stateOn"));
                item.SubItems.Add(b.Until.ToString("HH:mm", CultureInfo.InvariantCulture));
                item.SubItems.Add(b.Count.ToString(CultureInfo.InvariantCulture));
                item.SubItems.Add(b.User);
                item.SubItems.Add(b.Reason);
                item.BackColor = Color.FromArgb(0xFF, 0xF3, 0xCD);
            }
            else
            {
                item.SubItems.Add(_text.Text("BypassForm.stateOff"));
            }
            item.Selected = string.Equals(c.Code, selected, StringComparison.OrdinalIgnoreCase);
            _list.Items.Add(item);
        }
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        bool can = _session.Can(Permission.Bypass);
        string? code = SelectedCode;
        bool on = code is not null && _bypass.Active.Any(b =>
            string.Equals(b.Code, code, StringComparison.OrdinalIgnoreCase));
        bool guide = code is not null && _store.Current?.Commands.Any(c =>
            string.Equals(c.Code, code, StringComparison.OrdinalIgnoreCase) && CommandKind.IsGuide(c.Kind)) == true;
        _enable.Enabled = can && code is not null && !on && !guide;
        _disable.Enabled = can && on;
        _info.Text = can ? string.Empty : _text.Text("MainForm.needRole",
            _text.Text("role." + Permissions.Required(Permission.Bypass)));
    }

    private void Enable()
    {
        string? code = SelectedCode;
        if (code is null || !_session.Can(Permission.Bypass))
        {
            return;
        }
        string reason = _reason.Text.Trim();
        if (reason.Length < BypassService.MinReasonLength)
        {
            MessageBox.Show(this, _text.Text("BypassForm.reasonRequired", BypassService.MinReasonLength),
                Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        double hours = (double)_hours.Value;
        if (MessageBox.Show(this, _text.Text("BypassForm.confirm", code, hours), Text,
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) !=
            DialogResult.Yes)
        {
            return;
        }
        BypassEntry entry = _bypass.Enable(code, TimeSpan.FromHours(hours), reason, _session.UserName);
        _session.Record("bypass-on", code, new Dictionary<string, string>
        {
            ["hours"] = hours.ToString("0.0", CultureInfo.InvariantCulture),
            ["until"] = entry.Until.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            ["reason"] = reason,
        });
        _reason.Clear();
    }

    private void Disable()
    {
        string? code = SelectedCode;
        if (code is null || !_session.Can(Permission.Bypass))
        {
            return;
        }
        BypassEntry? entry = _bypass.Active.FirstOrDefault(b =>
            string.Equals(b.Code, code, StringComparison.OrdinalIgnoreCase));
        if (_bypass.Disable(code))
        {
            _session.Record("bypass-off", code, new Dictionary<string, string>
            {
                ["count"] = (entry?.Count ?? 0).ToString(CultureInfo.InvariantCulture),
            });
        }
    }
}
