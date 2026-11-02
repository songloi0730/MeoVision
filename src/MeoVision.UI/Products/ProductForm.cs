using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using MeoVision.App.Comm;
using MeoVision.App.Devices;
using MeoVision.App.Hosting;
using MeoVision.App.Products;
using MeoVision.App.Security;
using MeoVision.Core;
using MeoVision.Core.Localization;
using MeoVision.Core.Products;
using MeoVision.Core.Security;

namespace MeoVision.UI.Products;

/// <summary>
/// Form Sản phẩm (mockup v2.2): mọi sản phẩm trong thư mục dữ liệu, bảng lệnh của sản phẩm đang
/// chọn, và chuyển sản phẩm. Ai cũng xem được; chuyển cần Kỹ sư (Chương 19, mục 19.6) — bộ nạp kiểm
/// TOÀN BỘ trước khi đổi, hỏng thì giữ nguyên sản phẩm đang chạy.
/// </summary>
public sealed class ProductForm : Form
{
    private readonly ILocalizer _text;
    private readonly AppPaths _paths;
    private readonly ProductStore _store;
    private readonly Func<MachineView> _machine;
    private readonly Session _session;
    private readonly CommandService _commands;
    private readonly ListView _products = new()
    {
        Name = "products", Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true,
        MultiSelect = false, HideSelection = false,
    };
    private readonly ListView _table = new()
    {
        Name = "table", Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true,
    };
    private readonly Button _switch = new() { Name = "switchTo", Width = 220, Height = 44 };
    private readonly Label _info = new() { AutoSize = true, Padding = new Padding(0, 12, 0, 0) };

    public ProductForm(ILocalizer text, AppPaths paths, ProductStore store,
        Func<MachineView> machine, Session session, CommandService commands)
    {
        Guard.ThrowIfNull(text, nameof(text));
        Guard.ThrowIfNull(paths, nameof(paths));
        Guard.ThrowIfNull(store, nameof(store));
        Guard.ThrowIfNull(machine, nameof(machine));
        Guard.ThrowIfNull(session, nameof(session));
        Guard.ThrowIfNull(commands, nameof(commands));
        _text = text;
        _paths = paths;
        _store = store;
        _machine = machine;
        _session = session;
        _commands = commands;
        Name = "ProductForm";
        Font = new Font("Segoe UI", 10f);
        ScreenFit.Apply(this, new Size(1400, 760));

        foreach ((string name, int width) in new[] { ("colId", 110), ("colName", 200),
                     ("colCommands", 95), ("colVersion", 110), ("colModified", 140) })
        {
            _products.Columns.Add(new ColumnHeader { Name = name, Width = width });
        }
        foreach ((string name, int width) in new[] { ("colCode", 60), ("colName", 160), ("colKind", 80),
                     ("colChannel", 60), ("colCamera", 170), ("colJob", 150), ("colCalib", 80),
                     ("colCapture", 200), ("colLights", 150), ("colSlot", 50), ("colOutputs", 220) })
        {
            _table.Columns.Add(new ColumnHeader { Name = name, Width = width });
        }

        SplitContainer split = new()
        {
            Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, FixedPanel = FixedPanel.Panel1,
        };
        // Đặt sau khi form có kích thước thật — đặt trong khởi tạo thì bị co giãn theo tỉ lệ.
        Load += (_, _) => split.SplitterDistance = 220;
        split.Panel1.Controls.Add(_products);
        split.Panel2.Controls.Add(_table);
        Button openFolder = new() { Name = "openFolder", Width = 160, Height = 44 };
        Button close = new() { Name = "close", Width = 120, Height = 44, DialogResult = DialogResult.Cancel };
        FlowLayoutPanel bar = new() { Dock = DockStyle.Bottom, Height = 56, Padding = new Padding(6) };
        bar.Controls.Add(_switch);
        bar.Controls.Add(openFolder);
        bar.Controls.Add(close);
        bar.Controls.Add(_info);
        Controls.Add(split);
        Controls.Add(bar);
        CancelButton = close;

        _products.SelectedIndexChanged += (_, _) => ShowTable();
        _switch.Click += (_, _) => SwitchSelected();
        openFolder.Click += (_, _) => Process.Start("explorer.exe", "\"" + _paths.Products + "\"");
        UiText.Apply(this, _text);
        Reload();
    }

    private void Reload()
    {
        _products.Items.Clear();
        string current = _store.Current?.Id ?? string.Empty;
        foreach (ProductEntry p in ProductCatalog.List(_paths))
        {
            bool running = string.Equals(p.Id, current, StringComparison.OrdinalIgnoreCase);
            ListViewItem item = new((running ? "▶ " : "") + p.Id) { Tag = p };
            item.SubItems.Add(p.Error.Length > 0 ? _text.Text("ProductForm.broken", p.Error) : p.Name);
            item.SubItems.Add(p.Commands.ToString(CultureInfo.InvariantCulture));
            item.SubItems.Add(p.Version);
            item.SubItems.Add(p.Modified.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
            if (running)
            {
                item.Font = new Font(Font, FontStyle.Bold);
                item.Selected = true;
            }
            if (p.Error.Length > 0)
            {
                item.ForeColor = Color.Firebrick;
            }
            _products.Items.Add(item);
        }
        ShowTable();
    }

    private void ShowTable()
    {
        _table.Items.Clear();
        if (_products.SelectedItems.Count == 0 ||
            _products.SelectedItems[0].Tag is not ProductEntry { Config: { } config } entry)
        {
            _switch.Enabled = false;
            return;
        }
        foreach (CommandConfig c in config.Commands)
        {
            ListViewItem item = new(c.Code);
            item.SubItems.Add(c.Name);
            item.SubItems.Add(_text.Text(CommandKind.IsGuide(c.Kind) ? "ProductForm.kindGuide" : "ProductForm.kindInspect"));
            item.SubItems.Add(c.Channel);
            item.SubItems.Add(string.Join(" + ", new[] { c.Camera }
                .Concat(c.ExtraCameras.Select(e => e.Camera + "→" + e.Terminal))));
            item.SubItems.Add(c.Job);
            item.SubItems.Add(c.Calibration.Length == 0 ? "—" : c.Calibration);
            item.SubItems.Add(CaptureText(c));
            item.SubItems.Add(string.Join(", ", c.Lights.Select(l =>
                $"{l.Role}/{l.Channel}={l.Intensity}" + (l.SettleMs > 0 ? $" +{l.SettleMs}ms" : ""))));
            item.SubItems.Add(c.Display.Slot == 0 ? "—" : c.Display.Slot.ToString(CultureInfo.InvariantCulture));
            item.SubItems.Add(string.Join(", ", c.Outputs.Select(o => o.Name)));
            _table.Items.Add(item);
        }
        bool running = string.Equals(entry.Id, _store.Current?.Id, StringComparison.OrdinalIgnoreCase);
        _switch.Enabled = !running;
        _switch.Text = (_session.Can(Permission.SwitchProduct) ? "" : "🔒 ") +
                       _text.Text("ProductForm.switchTo");
        _info.Text = _text.Text("ProductForm.info", entry.Id, config.Commands.Count, entry.Version);
    }

    private static string CaptureText(CommandConfig c)
    {
        string Num(double? v) => v?.ToString("0.###", CultureInfo.InvariantCulture) ?? "—";
        if (c.ExposureMs is null && c.Brightness is null && c.Contrast is null)
        {
            return "—";   // giữ nguyên cài đặt của camera
        }
        return $"{Num(c.ExposureMs)} ms" +
               (c.Brightness is null ? "" : $" · B {Num(c.Brightness)}") +
               (c.Contrast is null ? "" : $" · C {Num(c.Contrast)}");
    }

    private void SwitchSelected()
    {
        if (_products.SelectedItems.Count == 0 ||
            _products.SelectedItems[0].Tag is not ProductEntry entry)
        {
            return;
        }
        if (!_session.Can(Permission.SwitchProduct))
        {
            MessageBox.Show(this, _text.Text("MainForm.needRole",
                _text.Text("role." + Permissions.Required(Permission.SwitchProduct))), Text);
            return;
        }
        string question = _text.Text(_commands.Online ? "ProductForm.confirmOnline"
            : "ProductForm.confirm", _store.Current?.Id ?? "—", entry.Id);
        if (MessageBox.Show(this, question, Text, MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }
        Cursor = Cursors.WaitCursor;
        try
        {
            ProductLoadResult result = _store.Switch(entry.Id, _machine(), _session.UserName);
            string problems = string.Join(Environment.NewLine,
                result.Problems.Take(12).Select(_text.Text));
            MessageBox.Show(this, result.Problems.Count == 0
                    ? _text.Text("ProductForm.switched", entry.Id)
                    : _text.Text("ProductForm.refused", entry.Id, result.Problems.Count) +
                      Environment.NewLine + problems,
                Text, MessageBoxButtons.OK,
                result.Problems.Count == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
        Reload();
    }
}
