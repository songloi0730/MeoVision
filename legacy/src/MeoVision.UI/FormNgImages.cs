// -------------------------------------------------------
// File:    FormNgImages.cs
// Project: MeoVision.UI
// Purpose: Xem/lọc ảnh NG đã lưu theo ngày — khép kín chính sách lưu ảnh đã dạy ở Ch14 §14.5
//          (INgImageArchiver) nhưng chưa từng có màn hình xem lại (DESIGN_LOG.md mục [8]).
// -------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using MeoVision.Application.Results;
using MeoVision.Core;

namespace MeoVision.UI;

public partial class FormNgImages : Form
{
    private readonly IInspectionResultStore _resultStore;
    private List<InspectionLogEntry> _filteredEntries = new();

    public FormNgImages(IInspectionResultStore resultStore)
    {
        Guard.ThrowIfNull(resultStore, nameof(resultStore));

        InitializeComponent();

        _resultStore = resultStore;

        dtpFrom.Value = DateTime.Today.AddDays(-7);
        dtpTo.Value = DateTime.Today;

        Load += async (_, _) => await RefreshAsync().ConfigureAwait(true);
        btnFilter.Click += async (_, _) => await RefreshAsync().ConfigureAwait(true);
        lstImages.SelectedIndexChanged += LstImages_SelectedIndexChanged;
    }

    private async System.Threading.Tasks.Task RefreshAsync()
    {
        IReadOnlyList<InspectionLogEntry> entries = await _resultStore.GetRecentAsync(500).ConfigureAwait(true);

        // Bao trọn ngày "Đến" (23:59:59) — DateTimePicker chỉ cho chọn ngày, người dùng mong đợi
        // ảnh CỦA ngày đó cũng hiện ra, không bị cắt ở 00:00.
        DateTimeOffset from = new(dtpFrom.Value.Date, TimeSpan.Zero);
        DateTimeOffset to = new(dtpTo.Value.Date.AddDays(1).AddTicks(-1), TimeSpan.Zero);

        _filteredEntries = entries
            .Where(e => e.SavedImagePath is not null
                && e.TimestampUtc.ToLocalTime() >= from
                && e.TimestampUtc.ToLocalTime() <= to)
            .ToList();

        lstImages.Items.Clear();
        foreach (InspectionLogEntry entry in _filteredEntries)
        {
            lstImages.Items.Add(
                $"{entry.TimestampUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}  {entry.Outcome}  score={entry.Score:F3}");
        }

        pictureBoxPreview.Image = null;
        lblPath.Text = _filteredEntries.Count == 0
            ? "Không có ảnh NG nào trong khoảng ngày đã chọn."
            : string.Empty;
    }

    private void LstImages_SelectedIndexChanged(object? sender, EventArgs e)
    {
        Image? previous = pictureBoxPreview.Image;
        pictureBoxPreview.Image = null;
        previous?.Dispose();

        int index = lstImages.SelectedIndex;
        if (index < 0 || index >= _filteredEntries.Count)
        {
            lblPath.Text = string.Empty;
            return;
        }

        string? path = _filteredEntries[index].SavedImagePath;
        if (path is null || !File.Exists(path))
        {
            lblPath.Text = "Không tìm thấy file ảnh (có thể đã bị xoá do retention).";
            return;
        }

        // Load bằng FileStream rồi tạo Bitmap độc lập — tránh khoá file gốc (File.Exists +
        // Image.FromFile giữ handle mở tới khi Dispose, gây phiền khi retention muốn xoá file này).
        using (FileStream stream = new(path, FileMode.Open, FileAccess.Read))
        {
            pictureBoxPreview.Image = new Bitmap(stream);
        }

        lblPath.Text = path;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            components?.Dispose();
            pictureBoxPreview.Image?.Dispose();
        }

        base.Dispose(disposing);
    }
}
