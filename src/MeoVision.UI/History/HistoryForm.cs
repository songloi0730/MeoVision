using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Cognex.VisionPro.Display;
using MeoVision.App.Audit;
using MeoVision.App.Results;
using MeoVision.Core;
using MeoVision.Core.Devices;
using MeoVision.Core.Localization;
using MeoVision.Core.Storage;
using MeoVision.UI.Display;
using MeoVision.Vision.Devices;

namespace MeoVision.UI.History;

/// <summary>
/// Lịch sử (Chương 23, mục 23.5): lọc theo ngày, lệnh, kết quả, serial; xem ảnh đã lưu; và — câu
/// hỏi của khách hàng khiếu nại — lệnh đó chạy với PHIÊN BẢN sản phẩm nào, ai áp dụng phiên bản đó,
/// lúc nào, vì sao. Mọi vai trò được xem.
/// </summary>
public sealed class HistoryForm : Form
{
    private readonly ILocalizer _text;
    private readonly ResultReader _reader;
    private readonly IImageStore _images;
    private readonly AuditLog _audit;
    private readonly DateTimePicker _day = new()
    {
        Format = DateTimePickerFormat.Short, Width = 140,
    };
    private readonly ComboBox _code = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList, Width = 90,
    };
    private readonly ComboBox _status = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList, Width = 100,
    };
    private readonly TextBox _serial = new() { Width = 180 };
    private readonly ListView _rows = new()
    {
        Name = "rows", Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true,
        HideSelection = false,
    };
    private readonly CogDisplay _display = new() { Dock = DockStyle.Fill };
    private readonly TextBox _detail = new()
    {
        Dock = DockStyle.Bottom, Height = 170, Multiline = true, ReadOnly = true,
        ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 10f),
    };
    private readonly Label _summary = new() { AutoSize = true, Padding = new Padding(0, 8, 0, 0) };
    private IReadOnlyList<ResultRow> _all = Array.Empty<ResultRow>();
    private CameraImage? _shown;

    public HistoryForm(ILocalizer text, ResultReader reader, IImageStore images, AuditLog audit)
    {
        Guard.ThrowIfNull(text, nameof(text));
        Guard.ThrowIfNull(reader, nameof(reader));
        Guard.ThrowIfNull(images, nameof(images));
        Guard.ThrowIfNull(audit, nameof(audit));
        _text = text;
        _reader = reader;
        _images = images;
        _audit = audit;
        Name = "HistoryForm";
        ScreenFit.Apply(this, new Size(1500, 880));
        Font = new Font("Segoe UI", 9.75f);

        FlowLayoutPanel filter = new()
        {
            Dock = DockStyle.Top, Height = 46, Padding = new Padding(6),
        };
        Button load = new() { Name = "load", Width = 90, Height = 32 };
        Button folder = new() { Name = "openFolder", Width = 150, Height = 32 };
        filter.Controls.AddRange(new Control[]
        {
            new Label { Name = "dayLabel", AutoSize = true, Padding = new Padding(0, 8, 0, 0) },
            _day,
            new Label { Name = "codeLabel", AutoSize = true, Padding = new Padding(0, 8, 0, 0) },
            _code,
            new Label { Name = "statusLabel", AutoSize = true, Padding = new Padding(0, 8, 0, 0) },
            _status,
            new Label { Name = "serialLabel", AutoSize = true, Padding = new Padding(0, 8, 0, 0) },
            _serial, load, folder, _summary,
        });
        foreach ((string name, int width) in new[] { ("colTime", 100), ("colCode", 50),
                     ("colSerial", 120), ("colStatus", 60), ("colValues", 260), ("colMs", 50),
                     ("colVersion", 110), ("colImage", 50) })
        {
            _rows.Columns.Add(new ColumnHeader { Name = name, Width = width });
        }
        SplitContainer split = new() { Dock = DockStyle.Fill };
        split.Panel1.Controls.Add(_rows);
        _display.Font = Control.DefaultFont;   // control Cognex: font mặc định (spike v2.1)
        RecordView.UseLightBackground(_display);
        split.Panel2.Controls.Add(_display);
        split.Panel2.Controls.Add(_detail);
        Controls.Add(split);
        Controls.Add(filter);

        // BYPASSED: sản phẩm chạy lúc lệnh đang bỏ qua kiểm (DESIGN_LOG [28] D1) — QA lọc ra để kiểm tay.
        _status.Items.AddRange(new object[] { "*", "OK", "NG", "ERROR", "BYPASSED" });
        _status.SelectedIndex = 0;
        load.Click += (_, _) => Reload();
        _day.ValueChanged += (_, _) => Reload();
        _code.SelectionChangeCommitted += (_, _) => Fill();
        _status.SelectionChangeCommitted += (_, _) => Fill();
        _serial.TextChanged += (_, _) => Fill();
        _rows.SelectedIndexChanged += (_, _) => ShowSelected();
        folder.Click += (_, _) => OpenFolder();
        FormClosed += (_, _) => ReleaseImage();
        UiText.Apply(this, text);
        foreach (ColumnHeader column in _rows.Columns)
        {
            column.Text = text.Text("HistoryForm." + column.Name);
        }
        Shown += (_, _) =>
        {
            split.SplitterDistance = Math.Max(400, split.Width - 680);   // ảnh + chi tiết đủ rộng
            Reload();
        };
    }

    /// <summary>Mở ở một ngày và lọc sẵn một serial (đường tắt từ dòng NG trên màn chính).</summary>
    public void ShowFor(DateTime day, string serial)
    {
        _day.Value = day.Date;
        _serial.Text = serial;
        Show();
        Activate();
    }

    private void Reload()
    {
        _all = _reader.Read(_day.Value.Date);
        string selected = _code.SelectedItem as string ?? "*";
        _code.Items.Clear();
        _code.Items.Add("*");
        foreach (string code in _all.Select(r => r.Code).Distinct().OrderBy(c => c))
        {
            _code.Items.Add(code);
        }
        _code.SelectedItem = _code.Items.Contains(selected) ? selected : "*";
        Fill();
    }

    private void Fill()
    {
        string code = _code.SelectedItem as string ?? "*";
        string status = _status.SelectedItem as string ?? "*";
        string serial = _serial.Text.Trim();
        List<ResultRow> rows = _all
            .Where(r => code == "*" || r.Code == code)
            .Where(r => status == "*" || r.Status == status)
            .Where(r => serial.Length == 0 ||
                        r.Serial.IndexOf(serial, StringComparison.OrdinalIgnoreCase) >= 0)
            .ToList();
        _rows.BeginUpdate();
        _rows.Items.Clear();
        foreach (ResultRow r in rows)
        {
            ListViewItem item = new(r.Time.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture))
            {
                Tag = r,
                ForeColor = r.Status == "OK" ? ForeColor : r.Status == "NG" ? Color.Firebrick
                    : r.Status == "BYPASSED" ? Color.DarkGoldenrod : Color.DarkOrange,
            };
            item.SubItems.Add(r.Code);
            item.SubItems.Add(r.Serial);
            item.SubItems.Add(r.Status);
            item.SubItems.Add(string.Join("  ", r.Values.Select(v => v.Name + " " + v.Value)));
            item.SubItems.Add(r.TotalMs.ToString(CultureInfo.InvariantCulture));
            item.SubItems.Add(r.Version);
            item.SubItems.Add(r.ImageFile.Length > 0 ? "✓" : "");
            _rows.Items.Add(item);
        }
        _rows.EndUpdate();
        _summary.Text = _text.Text("HistoryForm.summary", rows.Count,
            rows.Count(r => r.Status == "OK"), rows.Count(r => r.Status == "NG"),
            rows.Count(r => r.Status == "ERROR"), rows.Count(r => r.Status == "BYPASSED"));
    }

    private void ShowSelected()
    {
        if (_rows.SelectedItems.Count == 0 || _rows.SelectedItems[0].Tag is not ResultRow r)
        {
            return;
        }
        ReleaseImage();
        if (r.ImageFile.Length > 0 && File.Exists(r.ImageFile))
        {
            _shown = _images.Load(r.ImageFile);
            _display.Image = CogCameraImage.From(_shown);
            _display.Fit(true);
        }
        _detail.Text = Describe(r);
    }

    /// <summary>Kết quả + phiên bản sản phẩm đã chạy + ai đã đưa phiên bản đó vào chạy.</summary>
    private string Describe(ResultRow r)
    {
        List<string> lines = new()
        {
            $"{r.Time:yyyy-MM-dd HH:mm:ss.fff}  {r.Code}  {r.Serial}  {r.Status}  {r.Error}",
            string.Join("   ", r.Values.Select(v => v.Name + " = " + v.Value)),
            _text.Text("HistoryForm.ran", r.Product, r.Version, r.Channel, r.TotalMs),
        };
        // Phiên bản này vào chạy lúc nào, do ai, vì sao: tìm trong nhật ký thay đổi 60 ngày trước
        // đó.
        AuditEntry? loaded = _audit.Read(r.Time.AddDays(-60), r.Time)
            .LastOrDefault(e => e.Action == "product-loaded" &&
                                e.Data.TryGetValue("version", out string? v) && v == r.Version);
        AuditEntry? applied = loaded is null ? null : _audit.Read(loaded.Time.AddSeconds(-30),
                loaded.Time.AddSeconds(30))
            .LastOrDefault(e => e.Action is "apply" or "rollback");
        lines.Add(loaded is null
            ? _text.Text("HistoryForm.versionUnknown")
            : _text.Text("HistoryForm.versionLoaded", loaded.Time, loaded.Detail));
        if (applied is not null)
        {
            applied.Data.TryGetValue("reason", out string? reason);
            applied.Data.TryGetValue("changes", out string? changes);
            lines.Add(_text.Text("HistoryForm.versionApplied",
                _text.Text("audit.action." + applied.Action), applied.User, reason ?? "-"));
            if (!string.IsNullOrEmpty(changes))
            {
                lines.Add(changes);
            }
        }
        lines.Add(r.ImageFile.Length == 0 ? _text.Text("HistoryForm.noImage") : r.ImageFile);
        return string.Join(Environment.NewLine, lines);
    }

    private void OpenFolder()
    {
        if (_rows.SelectedItems.Count > 0 && _rows.SelectedItems[0].Tag is ResultRow r &&
            r.ImageFile.Length > 0)
        {
            Process.Start("explorer.exe", "/select,\"" + r.ImageFile + "\"");
        }
    }

    private void ReleaseImage()
    {
        _display.Image = null;   // gỡ khỏi khung trước rồi mới trả ảnh
        _shown?.Dispose();
        _shown = null;
    }
}
