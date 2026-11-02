using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using MeoVision.App;
using MeoVision.App.Hosting;
using MeoVision.App.Security;
using MeoVision.Core;
using MeoVision.Core.Localization;
using MeoVision.Core.Security;

namespace MeoVision.UI.Settings;

/// <summary>
/// Cài đặt → Lưu trữ và lưu ảnh (DESIGN_LOG [27]): thư mục ảnh / kết quả / log (ổ D...), lưu ảnh nào,
/// giữ bao lâu, ngưỡng dọn đĩa. Ghi vào <c>Data\settings.json</c> — chỉ các mục khác mặc định —
/// không bao giờ sửa <c>appsettings.json</c> của bản phát hành. Có hiệu lực sau khi mở lại.
/// Học từ RefVis-G (màn cài đặt hệ thống có nút chọn thư mục ảnh/dữ liệu, số ngày giữ, lưu ảnh OK/NG)
/// và RefVis-E (thư mục ảnh, log, dữ liệu tách riêng, mỗi loại số ngày giữ riêng).
/// </summary>
public sealed class StorageSettingsForm : Form
{
    private readonly ILocalizer _text;
    private readonly LoadedSettings _station;
    private readonly Session _session;
    private readonly string _programDirectory = AppContext.BaseDirectory;
    private readonly TextBox _images = PathBox("images");
    private readonly TextBox _results = PathBox("results");
    private readonly TextBox _logs = PathBox("logs");
    private readonly Label _imagesFree = FreeLabel();
    private readonly Label _resultsFree = FreeLabel();
    private readonly Label _logsFree = FreeLabel();
    private readonly CheckBox _saveNg = new() { Name = "saveNg", AutoSize = true };
    private readonly CheckBox _saveAnnotated = new() { Name = "saveAnnotated", AutoSize = true };
    private readonly NumericUpDown _okEvery = Number(0, 100000, 0);
    private readonly NumericUpDown _keepImageDays = Number(1, 3650, 0);
    private readonly NumericUpDown _keepResultDays = Number(1, 3650, 0);
    private readonly NumericUpDown _minFreeGb = Number(0, 100000, 1);
    private readonly NumericUpDown _dataMinFreeGb = Number(0, 100000, 1);
    private readonly Label _info = new() { AutoSize = true, MaximumSize = new Size(900, 0) };
    private readonly Button _save = new() { Name = "save", Width = 160, Height = 44 };

    public StorageSettingsForm(ILocalizer text, LoadedSettings station, Session session)
    {
        Guard.ThrowIfNull(text, nameof(text));
        Guard.ThrowIfNull(station, nameof(station));
        Guard.ThrowIfNull(session, nameof(session));
        _text = text;
        _station = station;
        _session = session;
        Name = "StorageSettingsForm";
        Font = new Font("Segoe UI", 10f);
        ScreenFit.Apply(this, new Size(1080, 640));

        TableLayoutPanel grid = new()
        {
            Dock = DockStyle.Fill, ColumnCount = 4, AutoScroll = true, Padding = new Padding(12),
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        PathRow(grid, "imagesLabel", _images, _imagesFree);
        PathRow(grid, "resultsLabel", _results, _resultsFree);
        PathRow(grid, "logsLabel", _logs, _logsFree);
        grid.Controls.Add(new Label
        {
            Name = "emptyHint", AutoSize = true, ForeColor = Color.DimGray,
            Padding = new Padding(0, 0, 0, 10),
        });
        grid.SetColumnSpan(grid.Controls[grid.Controls.Count - 1], 4);
        grid.Controls.Add(_saveNg);
        grid.SetColumnSpan(_saveNg, 4);
        grid.Controls.Add(_saveAnnotated);
        grid.SetColumnSpan(_saveAnnotated, 4);
        NumberRow(grid, "okEveryLabel", _okEvery);
        NumberRow(grid, "keepImageDaysLabel", _keepImageDays);
        NumberRow(grid, "keepResultDaysLabel", _keepResultDays);
        NumberRow(grid, "minFreeGbLabel", _minFreeGb);
        NumberRow(grid, "dataMinFreeGbLabel", _dataMinFreeGb);
        grid.Controls.Add(_info);
        grid.SetColumnSpan(_info, 4);

        Button defaults = new() { Name = "defaults", Width = 160, Height = 44 };
        Button close = new() { Name = "close", Width = 120, Height = 44, DialogResult = DialogResult.Cancel };
        FlowLayoutPanel bar = new() { Dock = DockStyle.Bottom, Height = 58, Padding = new Padding(8) };
        bar.Controls.Add(_save);
        bar.Controls.Add(defaults);
        bar.Controls.Add(close);
        Controls.Add(grid);
        Controls.Add(bar);
        CancelButton = close;

        _save.Click += (_, _) => SaveSettings();
        defaults.Click += (_, _) => Fill(StationSettings.Defaults(_programDirectory));
        foreach (TextBox box in new[] { _images, _results, _logs })
        {
            box.TextChanged += (_, _) => ShowFree();
        }
        UiText.Apply(this, _text);
        Text = _text.Text("StorageSettingsForm");
        Controls.Find("emptyHint", true)[0].Text = _text.Text("StorageSettingsForm.emptyHint",
            _station.Paths.Root);
        Fill(_station.Settings);
        bool can = _session.Can(Permission.StationSettings);
        _save.Enabled = can;
        _info.Text = can
            ? _text.Text("StorageSettingsForm.info", _station.StationFile)
            : _text.Text("MainForm.needRole", _text.Text("role." +
                Permissions.Required(Permission.StationSettings)));
    }

    private void Fill(AppSettings s)
    {
        _images.Text = s.ImagesRoot;
        _results.Text = s.ResultsRoot;
        _logs.Text = s.LogsRoot;
        _saveNg.Checked = s.Results.SaveNgImages;
        _saveAnnotated.Checked = s.Results.SaveAnnotatedImages;
        _okEvery.Value = Clamp(_okEvery, s.Results.SaveOkImagesEvery);
        _keepImageDays.Value = Clamp(_keepImageDays, s.Retention.KeepImageDays);
        _keepResultDays.Value = Clamp(_keepResultDays, s.Retention.KeepResultDays);
        _minFreeGb.Value = Clamp(_minFreeGb, s.Retention.ImageMinFreeGb);
        _dataMinFreeGb.Value = Clamp(_dataMinFreeGb, s.MinFreeDiskGb);
        ShowFree();
    }

    private void SaveSettings()
    {
        AppSettings edited = StationSettings.Copy(_station.Settings);
        edited.ImagesRoot = _images.Text.Trim();
        edited.ResultsRoot = _results.Text.Trim();
        edited.LogsRoot = _logs.Text.Trim();
        edited.Results.SaveNgImages = _saveNg.Checked;
        edited.Results.SaveAnnotatedImages = _saveAnnotated.Checked;
        edited.Results.SaveOkImagesEvery = (int)_okEvery.Value;
        edited.Retention.KeepImageDays = (int)_keepImageDays.Value;
        edited.Retention.KeepResultDays = (int)_keepResultDays.Value;
        edited.Retention.ImageMinFreeGb = (double)_minFreeGb.Value;
        edited.MinFreeDiskGb = (double)_dataMinFreeGb.Value;

        List<Msg> problems = new();
        foreach ((string label, string path) in new[]
                 {
                     ("images", edited.ImagesRoot), ("results", edited.ResultsRoot),
                     ("logs", edited.LogsRoot),
                 })
        {
            StorageCheck.Writable(Folder(path, label), problems);
        }
        // Ngưỡng dọn lớn hơn cả ổ ảnh = mỗi lần dọn xoá sạch ảnh các ngày trước (đo thật: ổ D của
        // máy viết sách 20 GB, ngưỡng mặc định 30 GB).
        DriveInfo? imageDrive = StorageCheck.DriveOf(Folder(edited.ImagesRoot, "images"));
        if (imageDrive is { IsReady: true } &&
            edited.Retention.ImageMinFreeGb >= imageDrive.TotalSize / 1e9 * 0.8)
        {
            problems.Add(Msg.Of("StorageSettingsForm.thresholdTooBig",
                edited.Retention.ImageMinFreeGb, imageDrive.Name, imageDrive.TotalSize / 1e9));
        }
        if (problems.Count > 0)
        {
            MessageBox.Show(this, string.Join(Environment.NewLine, problems.Select(_text.Text)),
                Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (imageDrive is { IsReady: true } &&
            imageDrive.AvailableFreeSpace / 1e9 < edited.Retention.ImageMinFreeGb &&
            MessageBox.Show(this, _text.Text("StorageSettingsForm.belowThreshold",
                    imageDrive.Name, imageDrive.AvailableFreeSpace / 1e9,
                    edited.Retention.ImageMinFreeGb), Text, MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        IReadOnlyDictionary<string, (string Old, string New)> changes =
            StationSettings.Save(_programDirectory, _station.StationFile, edited);
        if (changes.Count > 0)
        {
            // Ai đổi gì, từ giá trị nào sang giá trị nào (RefVis-D bản 3.8 thêm đúng sự kiện này).
            _session.Record("settings-changed", string.Join(", ", changes.Keys),
                changes.ToDictionary(c => c.Key, c => c.Value.Old + " -> " + c.Value.New));
        }
        MessageBox.Show(this, _text.Text("StorageSettingsForm.saved", changes.Count,
            _station.StationFile), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    /// <summary>Đường dẫn đầy đủ của một ô; ô trống = thư mục con trong Data.</summary>
    private string Folder(string configured, string inData) =>
        configured.Length == 0
            ? Path.Combine(_station.Paths.Root, inData)
            : Path.GetFullPath(Path.IsPathRooted(Environment.ExpandEnvironmentVariables(configured))
                ? Environment.ExpandEnvironmentVariables(configured)
                : Path.Combine(_programDirectory, Environment.ExpandEnvironmentVariables(configured)));

    private void ShowFree()
    {
        (TextBox Box, Label Free, string InData)[] rows =
        {
            (_images, _imagesFree, "images"), (_results, _resultsFree, "results"),
            (_logs, _logsFree, "logs"),
        };
        foreach ((TextBox box, Label free, string inData) in rows)
        {
            DriveInfo? drive = null;
            try
            {
                drive = StorageCheck.DriveOf(Folder(box.Text.Trim(), inData));
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or
                                           PathTooLongException)
            {
                // Đang gõ dở đường dẫn.
            }
            free.ForeColor = drive is { IsReady: true } ? ForeColor : Color.Firebrick;
            free.Text = drive is { IsReady: true }
                ? _text.Text("StorageSettingsForm.free", drive.Name,
                    drive.AvailableFreeSpace / 1e9, drive.TotalSize / 1e9)
                : _text.Text("StorageSettingsForm.noDrive");
        }
    }

    private void PathRow(TableLayoutPanel grid, string label, TextBox box, Label free)
    {
        Button browse = new() { Name = "browse", Width = 110, Height = 32 };
        browse.Click += (_, _) =>
        {
            using FolderBrowserDialog dialog = new() { SelectedPath = box.Text };
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                box.Text = dialog.SelectedPath;
            }
        };
        grid.Controls.Add(new Label { Name = label, AutoSize = true, Padding = new Padding(0, 8, 8, 0) });
        grid.Controls.Add(box);
        grid.Controls.Add(browse);
        grid.Controls.Add(free);
    }

    private static void NumberRow(TableLayoutPanel grid, string label, NumericUpDown value)
    {
        grid.Controls.Add(new Label { Name = label, AutoSize = true, Padding = new Padding(0, 8, 8, 0) });
        grid.Controls.Add(value);
        grid.SetColumnSpan(value, 3);
    }

    private static TextBox PathBox(string name) => new() { Name = name, Dock = DockStyle.Fill };

    private static Label FreeLabel() => new() { AutoSize = true, Padding = new Padding(8, 8, 0, 0) };

    private static NumericUpDown Number(decimal min, decimal max, int decimals) =>
        new() { Minimum = min, Maximum = max, DecimalPlaces = decimals, Width = 120 };

    private static decimal Clamp(NumericUpDown box, double value) =>
        Math.Min(box.Maximum, Math.Max(box.Minimum, (decimal)value));
}

/// <summary>Kiểm một thư mục ghi được thật (tạo + ghi thử + xoá file thử), như Code 17.5.</summary>
public static class StorageCheck
{
    public static void Writable(string folder, List<Msg> problems)
    {
        Guard.ThrowIfNull(problems, nameof(problems));
        try
        {
            Directory.CreateDirectory(folder);
            string probe = Path.Combine(folder, ".write-test-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                       ArgumentException or NotSupportedException)
        {
            problems.Add(Msg.Of("startup.dataFolder.notWritable", folder, ex.Message));
        }
    }

    public static DriveInfo? DriveOf(string folder)
    {
        string? root = Path.GetPathRoot(folder);
        return string.IsNullOrEmpty(root) || root!.StartsWith(@"\\", StringComparison.Ordinal)
            ? null
            : new DriveInfo(root);
    }
}
