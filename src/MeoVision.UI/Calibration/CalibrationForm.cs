using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using MeoVision.App.Calibration;
using MeoVision.App.Hosting;
using MeoVision.App.Security;
using MeoVision.Core;
using MeoVision.Core.Localization;
using MeoVision.Core.Security;

namespace MeoVision.UI.Calibration;

/// <summary>
/// Hiệu chuẩn (DESIGN_LOG [28] D5). Máy dẫn hiệu chuẩn qua kênh (CS/CP/CE, Chương 21); form này cho
/// người thấy kết quả: hiệu chuẩn nào của máy, lưu lúc nào, RMS lần tính gần nhất và sai số TỪNG điểm
/// (điểm nào kéo RMS lên), phiên máy đang dẫn dở; và hai thao tác: dùng lại bản cũ, bỏ phiên dở. Học từ
/// RefVis-A, RefVis-C (form hiệu chuẩn có bảng điểm, lưu ảnh từng điểm).
/// </summary>
public sealed class CalibrationForm : Form
{
    private readonly ILocalizer _text;
    private readonly CalibrationService _service;
    private readonly AppPaths _paths;
    private readonly Session _session;
    private readonly ListView _list = new()
    {
        Name = "calibrations", Dock = DockStyle.Top, Height = 170, View = View.Details, FullRowSelect = true,
        MultiSelect = false, HideSelection = false,
    };
    private readonly ListView _points = new()
    {
        Name = "points", Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true,
    };
    private readonly ListBox _history = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly Button _restore = new() { Name = "restore", Width = 200, Height = 40 };
    private readonly Button _cancelSession = new() { Name = "cancelSession", Width = 200, Height = 40 };
    private readonly Label _info = new() { AutoSize = true, MaximumSize = new Size(1100, 0) };
    private readonly Timer _timer = new() { Interval = 1000 };

    public CalibrationForm(ILocalizer text, CalibrationService service, AppPaths paths, Session session)
    {
        Guard.ThrowIfNull(text, nameof(text));
        Guard.ThrowIfNull(service, nameof(service));
        Guard.ThrowIfNull(paths, nameof(paths));
        Guard.ThrowIfNull(session, nameof(session));
        _text = text;
        _service = service;
        _paths = paths;
        _session = session;
        Name = "CalibrationForm";
        Font = new Font("Segoe UI", 10f);
        ScreenFit.Apply(this, new Size(1300, 780));

        foreach ((string name, int width) in new[] { ("colId", 110), ("colCamera", 110),
                     ("colSaved", 150), ("colLastAttempt", 150), ("colPoints", 70), ("colRms", 120),
                     ("colHistory", 80), ("colSession", 160) })
        {
            _list.Columns.Add(new ColumnHeader { Name = name, Width = width });
        }
        foreach ((string name, int width) in new[] { ("colIndex", 60), ("colPixelX", 110),
                     ("colPixelY", 110), ("colMachineX", 110), ("colMachineY", 110), ("colResidual", 120) })
        {
            _points.Columns.Add(new ColumnHeader { Name = name, Width = width });
        }
        SplitContainer split = new() { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel2 };
        split.Panel1.Controls.Add(_points);
        Panel right = new() { Dock = DockStyle.Fill, Padding = new Padding(6, 0, 0, 0) };
        right.Controls.Add(_history);
        right.Controls.Add(new Label { Name = "historyLabel", Dock = DockStyle.Top, Height = 26 });
        split.Panel2.Controls.Add(right);
        Load += (_, _) => split.SplitterDistance = Math.Max(300, split.Width - 380);

        Button openFolder = new() { Name = "openFolder", Width = 160, Height = 40 };
        Button close = new() { Name = "close", Width = 110, Height = 40, DialogResult = DialogResult.Cancel };
        FlowLayoutPanel bar = new() { Dock = DockStyle.Bottom, Height = 90, Padding = new Padding(6) };
        bar.Controls.Add(_restore);
        bar.Controls.Add(_cancelSession);
        bar.Controls.Add(openFolder);
        bar.Controls.Add(close);
        bar.SetFlowBreak(close, true);
        bar.Controls.Add(_info);
        Controls.Add(split);
        Controls.Add(_list);
        Controls.Add(bar);
        CancelButton = close;

        _list.SelectedIndexChanged += (_, _) => ShowSelected();
        _history.SelectedIndexChanged += (_, _) => UpdateButtons();
        _restore.Click += (_, _) => Restore();
        _cancelSession.Click += (_, _) => CancelSession();
        openFolder.Click += (_, _) => Process.Start("explorer.exe", "\"" +
            (SelectedId is { } id ? Path.Combine(_paths.MachineCalibration, id) : _paths.MachineCalibration) + "\"");
        _timer.Tick += (_, _) => Reload();   // phiên máy đang dẫn: số điểm tăng dần
        FormClosed += (_, _) => _timer.Stop();
        UiText.Apply(this, _text);
        Reload();
        _timer.Start();
    }

    private string? SelectedId => _list.SelectedItems.Count == 0 ? null : (string)_list.SelectedItems[0].Tag;

    private void Reload()
    {
        string? selected = SelectedId;
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (CalibrationInfo c in _service.List())
        {
            ListViewItem item = new(c.Id) { Tag = c.Id };
            item.SubItems.Add(c.Camera == "?" ? "—" : c.Camera);   // chưa có calibration.json
            item.SubItems.Add(When(c.SavedAt));
            item.SubItems.Add(When(c.LastAttemptAt));
            item.SubItems.Add(c.LastPoints.ToString(CultureInfo.InvariantCulture));
            item.SubItems.Add(c.LastRms is { } rms
                ? rms.ToString("0.000", CultureInfo.InvariantCulture) + " / " +
                  c.MaxRms.ToString("0.###", CultureInfo.InvariantCulture)
                : "—");
            item.SubItems.Add(c.HistoryCount.ToString(CultureInfo.InvariantCulture));
            item.SubItems.Add(c.PointsInSession is { } n ? _text.Text("CalibrationForm.inSession", n) : "");
            // Lần tính gần nhất bị từ chối (RMS vượt ngưỡng) hoặc mới hơn bản đang dùng: tô để người ta hỏi.
            if (c.LastRms > c.MaxRms)
            {
                item.ForeColor = Color.Firebrick;
            }
            item.Selected = c.Id == selected;
            _list.Items.Add(item);
        }
        _list.EndUpdate();
        if (_list.SelectedItems.Count == 0 && _list.Items.Count > 0)
        {
            _list.Items[0].Selected = true;
        }
        if (_list.Items.Count == 0)
        {
            _info.Text = _text.Text("CalibrationForm.none", _paths.MachineCalibration);
        }
        UpdateButtons();
    }

    private void ShowSelected()
    {
        _points.Items.Clear();
        _history.Items.Clear();
        if (SelectedId is not { } id)
        {
            return;
        }
        IReadOnlyList<CalibrationPointRow> rows = _service.Points(id);
        double worst = rows.Count == 0 ? 0 : rows.Max(r => double.IsNaN(r.Residual) ? 0 : r.Residual);
        foreach (CalibrationPointRow r in rows)
        {
            ListViewItem item = new(r.Index.ToString(CultureInfo.InvariantCulture));
            foreach (double v in new[] { r.PixelX, r.PixelY, r.MachineX, r.MachineY, r.Residual })
            {
                item.SubItems.Add(v.ToString("0.000", CultureInfo.InvariantCulture));
            }
            if (rows.Count > 1 && r.Residual >= worst && worst > 0)
            {
                item.ForeColor = Color.Firebrick;   // điểm kéo RMS lên nhiều nhất
            }
            _points.Items.Add(item);
        }
        foreach (string file in _service.History(id))
        {
            _history.Items.Add(file);
        }
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        bool can = _session.Can(Permission.CommandSetup);
        CalibrationInfo? info = SelectedId is { } id ? _service.List().FirstOrDefault(c => c.Id == id) : null;
        _restore.Enabled = can && _history.SelectedItem is string;
        _cancelSession.Enabled = can && info?.PointsInSession is not null;
        if (!can)
        {
            _info.Text = _text.Text("MainForm.needRole",
                _text.Text("role." + Permissions.Required(Permission.CommandSetup)));
        }
    }

    private void Restore()
    {
        if (SelectedId is not { } id || _history.SelectedItem is not string file ||
            !_session.Can(Permission.CommandSetup))
        {
            return;
        }
        if (MessageBox.Show(this, _text.Text("CalibrationForm.confirmRestore", id, file), Text,
                MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) !=
            DialogResult.Yes)
        {
            return;
        }
        Cursor = Cursors.WaitCursor;
        Msg? problem;
        try
        {
            problem = _service.RestoreFromHistory(id, file, _session.UserName);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
        _session.Record(problem is null ? "calibration-restored" : "calibration-restore-refused", id,
            new Dictionary<string, string> { ["file"] = file });
        MessageBox.Show(this, problem is null ? _text.Text("CalibrationForm.restored", id, file)
            : _text.Text(problem), Text, MessageBoxButtons.OK,
            problem is null ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        Reload();
        ShowSelected();
    }

    private void CancelSession()
    {
        if (SelectedId is not { } id || !_session.Can(Permission.CommandSetup))
        {
            return;
        }
        if (_service.CancelSession(id))
        {
            _session.Record("calibration-cancelled", id);
        }
        Reload();
    }

    private static string When(DateTime? time) =>
        time?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "—";
}
