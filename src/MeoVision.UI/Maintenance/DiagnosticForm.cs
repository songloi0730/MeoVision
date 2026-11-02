using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows.Forms;
using MeoVision.App.Maintenance;
using MeoVision.App.Products;
using MeoVision.App.Security;
using MeoVision.Core;
using MeoVision.Core.Localization;

namespace MeoVision.UI.Maintenance;

/// <summary>
/// Xuất gói chẩn đoán (Chương 24, mục 24.5): một ô ghi chú — thứ log không ghi được ("máy kêu lạ
/// trước khi lỗi") — và phạm vi mặc định hẹp; mở rộng là lựa chọn tường minh. Gói luôn nằm ở MỘT
/// thư mục cố định, để người hỗ trợ không phải hỏi "lưu ở đâu".
/// </summary>
public sealed class DiagnosticForm : Form
{
    private readonly ILocalizer _text;
    private readonly DiagnosticBundle _bundle;
    private readonly Func<string> _systemReport;
    private readonly ProductStore _store;
    private readonly Session _session;
    private readonly TextBox _note = new()
    {
        Name = "note", Multiline = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical,
    };
    private readonly NumericUpDown _days = new() { Minimum = 1, Maximum = 30, Value = 2 };
    private readonly NumericUpDown _images = new() { Minimum = 0, Maximum = 500, Value = 20 };
    private readonly CheckBox _allProducts = new() { Name = "allProducts", AutoSize = true };
    private readonly Label _result = new() { AutoSize = true, MaximumSize = new Size(640, 0) };
    private readonly Button _export = new() { Name = "export", Width = 200, Height = 44 };
    private readonly Button _openFolder = new()
    {
        Name = "openFolder", Width = 200, Height = 44, Enabled = false,
    };
    private string? _lastFile;

    public DiagnosticForm(ILocalizer text, DiagnosticBundle bundle, Func<string> systemReport,
        ProductStore store, Session session)
    {
        Guard.ThrowIfNull(text, nameof(text));
        Guard.ThrowIfNull(bundle, nameof(bundle));
        Guard.ThrowIfNull(systemReport, nameof(systemReport));
        Guard.ThrowIfNull(store, nameof(store));
        Guard.ThrowIfNull(session, nameof(session));
        _text = text;
        _bundle = bundle;
        _systemReport = systemReport;
        _store = store;
        _session = session;
        Name = "DiagnosticForm";
        Size = new Size(700, 560);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        Font = new Font("Segoe UI", 11f);

        TableLayoutPanel grid = new()
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 6, Padding = new Padding(12),
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Label noteLabel = new() { Name = "noteLabel", AutoSize = true };
        grid.Controls.Add(noteLabel, 0, 0);
        grid.SetColumnSpan(noteLabel, 2);
        grid.Controls.Add(_note, 0, 1);
        grid.SetColumnSpan(_note, 2);
        grid.Controls.Add(new Label { Name = "daysLabel", AutoSize = true }, 0, 2);
        grid.Controls.Add(_days, 1, 2);
        grid.Controls.Add(new Label { Name = "imagesLabel", AutoSize = true }, 0, 3);
        grid.Controls.Add(_images, 1, 3);
        grid.Controls.Add(_allProducts, 0, 4);
        grid.SetColumnSpan(_allProducts, 2);
        FlowLayoutPanel actions = new() { AutoSize = true, Dock = DockStyle.Fill };
        actions.Controls.Add(_export);
        actions.Controls.Add(_openFolder);
        actions.Controls.Add(_result);
        grid.Controls.Add(actions, 0, 5);
        grid.SetColumnSpan(actions, 2);
        Controls.Add(grid);

        _export.Click += OnExport;
        _openFolder.Click += (_, _) =>
        {
            if (_lastFile is not null)
            {
                Process.Start("explorer.exe", "/select,\"" + _lastFile + "\"");
            }
        };
        UiText.Apply(this, _text);
    }

    private async void OnExport(object? sender, EventArgs e)
    {
        string note = _note.Text.Trim();
        DiagnosticOptions options = new()
        {
            Days = (int)_days.Value, MaxImages = (int)_images.Value,
            AllProducts = _allProducts.Checked,
        };
        string? product = _store.Current?.Id;
        _export.Enabled = false;
        Cursor = Cursors.WaitCursor;
        try
        {
            string system = _systemReport();
            DiagnosticBundleResult r = await Task.Run(() =>
                _bundle.Export(note, system, product, options, DateTime.Now));
            _lastFile = r.File;
            _openFolder.Enabled = true;
            _result.ForeColor = ForeColor;
            _result.Text = _text.Text("DiagnosticForm.done", r.File,
                (r.Bytes / 1e6).ToString("0.0", CultureInfo.InvariantCulture), r.Entries,
                r.ElapsedMs);
            _session.Record("diagnostics-exported", note, new Dictionary<string, string>
            {
                ["file"] = r.File, ["bytes"] = r.Bytes.ToString(CultureInfo.InvariantCulture),
                ["days"] = options.Days.ToString(CultureInfo.InvariantCulture),
                ["images"] = options.MaxImages.ToString(CultureInfo.InvariantCulture),
                ["allProducts"] = options.AllProducts ? "yes" : "no",
            });
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            _result.ForeColor = Color.Firebrick;
            _result.Text = _text.Text("DiagnosticForm.failed", ex.Message);
        }
        finally
        {
            _export.Enabled = true;
            Cursor = Cursors.Default;
        }
    }
}
