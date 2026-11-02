using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Cognex.VisionPro;
using MeoVision.App.Commands;
using MeoVision.App.Hosting;
using MeoVision.App.Products;
using MeoVision.App.Security;
using MeoVision.Core.Security;
using MeoVision.Core;
using MeoVision.Core.Commands;
using MeoVision.Core.Devices;
using MeoVision.Core.Localization;
using MeoVision.Core.Products;
using MeoVision.UI.Display;
using MeoVision.Vision.Devices;

namespace MeoVision.UI.Setup;

/// <summary>
/// "Thiết lập lệnh" (Chương 22, mục 22.7): sửa một lệnh trên BẢN NHÁP trong khi máy vẫn chạy.
/// Trái: các lệnh. Giữa: nguồn ảnh (lệnh gần nhất trong RAM, ảnh khác của chạy kèm, thư mục) và
/// kết quả bản nháp cạnh kết quả bản đang chạy. Phải: Job (tham số nhanh, editor VisionPro) · Kết
/// quả &amp; giới hạn · Ảnh &amp; đèn · Bộ ảnh thử. Đáy: chạy kèm trên sản phẩm thật, Khác biệt,
/// Huỷ, Quay lại bản trước, Áp dụng. Cửa sổ không modal — màn chính và lệnh của máy chạy phía sau.
/// </summary>
public sealed class SetupForm : Form
{
    private readonly ILocalizer _text;
    private readonly ProductStore _store;
    private readonly ProductBackup _backup;
    private readonly ShadowRunner _shadow;
    private readonly RecentFrames _recent;
    private readonly Func<MachineView> _machine;
    private readonly AppPaths _paths;

    private readonly ListBox _commands = new() { Name = "commands", Dock = DockStyle.Fill };
    private readonly ComboBox _recentBox = new()
    {
        Name = "recentBox", DropDownStyle = ComboBoxStyle.DropDownList, Width = 330,
    };
    private readonly Label _sourceName = new()
    {
        AutoSize = true, Padding = new Padding(0, 8, 0, 0),
    };
    private readonly CogRecordsDisplay _display = new() { Dock = DockStyle.Fill };
    private readonly Label _runningLine = new() { Dock = DockStyle.Top, Height = 24 };
    private readonly Label _draftLine = new() { Dock = DockStyle.Top, Height = 24 };
    private readonly FlowLayoutPanel _tunables = new()
    {
        Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false,
        AutoScroll = true,
    };
    private readonly DataGridView _limits = new() { Name = "limits", Dock = DockStyle.Fill };
    private readonly CheckBox _cameraDefault = new() { Name = "cameraDefault", AutoSize = true };
    private readonly NumericUpDown _exposure = new()
    {
        DecimalPlaces = 2, Minimum = 0.01m, Maximum = 1000, Increment = 0.1m, Width = 100,
    };
    private readonly DataGridView _lights = new() { Name = "lights", Height = 200, Width = 380 };
    private readonly TextBox _testFolder = new() { Width = 300 };
    private readonly ListView _testResults = new()
    {
        Name = "testResults", Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true,
    };
    private readonly Label _testSummary = new() { Dock = DockStyle.Bottom, Height = 44 };
    private readonly NumericUpDown _replayDays = new()
    {
        Minimum = 1, Maximum = 365, Value = 7, Width = 60,
    };
    private readonly ReplayRunner _replay;
    private readonly Dictionary<string, ReplayReport> _replays =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly CheckBox _shadowOn = new() { Name = "shadowOn", AutoSize = true };
    private readonly Label _shadowCounters = new()
    {
        AutoSize = true, Padding = new Padding(0, 8, 0, 0),
    };
    private readonly Label _captureWarning = new()
    {
        AutoSize = true, ForeColor = Color.DarkOrange, Padding = new Padding(0, 8, 0, 0),
    };
    private readonly System.Windows.Forms.Timer _rerun = new() { Interval = 150 };
    private readonly Session _session;
    private readonly List<Control> _editing = new();
    private readonly Label _locked = new()
    {
        Dock = DockStyle.Top, Height = 34, BackColor = Color.Firebrick, ForeColor = Color.White,
        TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(8, 0, 0, 0), Visible = false,
        Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
    };

    private readonly List<CommandResult> _recentItems = new();
    private DraftSession? _draft;
    private string _code = string.Empty;
    private DisplayFrame? _source;
    private CommandResult? _running;
    private CommandResult? _draftResult;
    private FolderCamera? _folder;
    private string? _lastSnapshot;
    private int _differenceIndex;
    private bool _busy;
    private bool _again;

    public SetupForm(ILocalizer text, ProductStore store, ProductBackup backup,
        ShadowRunner shadow, RecentFrames recent, Func<MachineView> machine, AppPaths paths,
        Session session, ReplayRunner replay)
    {
        Guard.ThrowIfNull(text, nameof(text));
        Guard.ThrowIfNull(store, nameof(store));
        Guard.ThrowIfNull(backup, nameof(backup));
        Guard.ThrowIfNull(shadow, nameof(shadow));
        Guard.ThrowIfNull(recent, nameof(recent));
        Guard.ThrowIfNull(machine, nameof(machine));
        Guard.ThrowIfNull(paths, nameof(paths));
        Guard.ThrowIfNull(session, nameof(session));
        Guard.ThrowIfNull(replay, nameof(replay));
        _replay = replay;
        _session = session;
        _text = text;
        _store = store;
        _backup = backup;
        _shadow = shadow;
        _recent = recent;
        _machine = machine;
        _paths = paths;
        Name = "SetupForm";
        ScreenFit.Apply(this, new Size(1560, 900));
        Font = new Font("Segoe UI", 9.75f);
        BuildLayout();

        _commands.SelectedIndexChanged += (_, _) =>
        {
            if (_commands.SelectedItem is CommandItem item && item.Code != _code)
            {
                SelectCommand(item.Code, null);
            }
        };
        _recentBox.DropDown += (_, _) => FillRecent();
        _recentBox.SelectionChangeCommitted += (_, _) =>
        {
            if (_recentBox.SelectedIndex >= 0 && _recentBox.SelectedIndex < _recentItems.Count)
            {
                CommandResult item = _recentItems[_recentBox.SelectedIndex];
                SetSource(item.Frame!.Share(), item, "SetupForm.source.recent");
            }
        };
        _rerun.Tick += (_, _) =>
        {
            _rerun.Stop();
            RunDraft();
        };
        _shadowOn.CheckedChanged += (_, _) => _shadow.Attach(_shadowOn.Checked ? _draft : null);
        _shadow.Compared += OnShadowCompared;
        _session.Changed += OnSessionChanged;
        _text.LanguageChanged += OnLanguageChanged;
        FormClosing += OnClosing;
        UiText.Apply(this, _text);
        ApplyRole();
    }

    /// <summary>
    /// Hiện form cho một lệnh, kèm kết quả đang hiện trên màn chính (nút ⚙ trên khung). Mở bản nháp
    /// ở lần đầu; bản nháp đang sửa thì giữ nguyên. Khung của <paramref name="shown"/> (đã Share)
    /// thuộc về form từ đây.
    /// </summary>
    public void OpenFor(string? code, CommandResult? shown)
    {
        if (_draft is null && !OpenDraft())
        {
            shown?.Frame?.Dispose();
            return;
        }
        string target = code ?? _code;
        if (target.Length == 0 || _draft!.Command(target) is null)
        {
            target = _draft!.Snapshot().Commands.First().Code;
        }
        HighlightCommand(target);
        SelectCommand(target, shown);
        Show();
        Activate();
    }

    private bool OpenDraft()
    {
        DraftOpenResult opened;
        try
        {
            opened = _store.OpenDraft(_machine(), _session.UserName);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            // Chép job sang bản nháp hỏng (ổ đầy, file bị khoá): báo rõ, không văng lỗi ra ngoài.
            MessageBox.Show(this, _text.Text("SetupForm.draftFailed", ex.Message), Text,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        if (opened.Draft is null)
        {
            MessageBox.Show(this, string.Join(Environment.NewLine,
                    opened.Problems.Take(10).Select(_text.Text)), Text,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        _draft = opened.Draft;
        _commands.Items.Clear();
        foreach (CommandConfig c in _draft.Snapshot().Commands)
        {
            _commands.Items.Add(new CommandItem(c.Code, c.Name));
        }
        _shadow.Attach(_shadowOn.Checked ? _draft : null);
        UpdateShadowCounters();
        return true;
    }

    private void ReopenDraft()
    {
        _shadow.Attach(null);
        _draft?.Dispose();
        _draft = null;
        if (OpenDraft())
        {
            SelectCommand(_code, null);
        }
    }

    private void SelectCommand(string code, CommandResult? shown)
    {
        _code = code;
        _differenceIndex = 0;
        CommandConfig c = _draft!.Command(code)!;
        Text = _text.Text("SetupForm.title", _draft.ProductId, code, c.Name);
        BuildTunables(c);
        FillLimits(c);
        FillCapture(c);
        _testFolder.Text = Path.Combine(_paths.Product(_draft.ProductId).Samples, code);
        CloseFolder();
        if (shown?.Frame is not null)
        {
            SetSource(shown.Frame, shown, "SetupForm.source.main");
        }
        else
        {
            shown?.Frame?.Dispose();
            FillRecent();
            if (_recentItems.Count > 0)
            {
                _recentBox.SelectedIndex = 0;
                SetSource(_recentItems[0].Frame!.Share(), _recentItems[0],
                    "SetupForm.source.recent");
            }
            else
            {
                SetSource(null, null, "SetupForm.source.none");
            }
        }
        UpdateCaptureWarning();
    }

    // ---------------- nguồn ảnh ----------------

    /// <summary><paramref name="frame"/> thuộc về form từ đây (đã Share bởi người gọi).</summary>
    private void SetSource(DisplayFrame? frame, CommandResult? running, string sourceKey)
    {
        DisplayFrame? old = _source;
        _source = frame;
        _running = running;
        if (sourceKey != "SetupForm.source.recent")
        {
            _recentBox.SelectedIndex = -1;   // ô "lệnh gần nhất" không còn là nguồn đang xem
        }
        _sourceName.Text = _text.Text(sourceKey) +
                           (frame is null ? string.Empty : "  " + frame.Image.Source);
        _runningLine.Text = running is null
            ? _text.Text("SetupForm.running.none")
            : _text.Text("SetupForm.running", Verdict(running), ResultFrameView.Describe(_text,
                running));
        if (frame is null)
        {
            ShowDraft(null);
        }
        else
        {
            RunDraft();
        }
        old?.Dispose();   // sau khi khung đã đổi sang ảnh mới (RunDraft vẽ ảnh mới khi xong)
    }

    private void FillRecent()
    {
        foreach (CommandResult r in _recentItems)
        {
            r.Frame?.Dispose();
        }
        _recentItems.Clear();
        _recentBox.Items.Clear();
        if (_code.Length == 0)
        {
            return;
        }
        foreach (CommandResult r in _recent.Take(_code))
        {
            _recentItems.Add(r);
            _recentBox.Items.Add(r.Frame!.Image.CapturedAt.ToString("HH:mm:ss.fff",
                CultureInfo.InvariantCulture) + "  " + Verdict(r) + "  " +
                ResultFrameView.Describe(_text, r));
        }
    }

    private async void OnOpenFolder(object? sender, EventArgs e)
    {
        using FolderBrowserDialog dialog = new() { SelectedPath = _testFolder.Text };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }
        CloseFolder();
        _folder = new FolderCamera("setup", dialog.SelectedPath, ImagePatterns, loop: true,
            toGrey: true);
        try
        {
            await _folder.OpenAsync(CancellationToken.None);
        }
        catch (DeviceException ex)
        {
            MessageBox.Show(this, _text.Text(ex.Info), Text);
            CloseFolder();
            return;
        }
        await NextFolderImage();
    }

    private async Task NextFolderImage()
    {
        if (_folder is null || _draft is null)
        {
            return;
        }
        try
        {
            CameraImage image = await _folder.GrabAsync(CaptureSettings.Default,
                CancellationToken.None);
            _draft.Calibrate(_code, image);   // ảnh từ file chưa có hệ toạ độ mm của lệnh
            SetSource(new DisplayFrame(image, null), null, "SetupForm.source.folder");
        }
        catch (DeviceException ex)
        {
            MessageBox.Show(this, _text.Text(ex.Info), Text);
        }
    }

    private void CloseFolder()
    {
        _folder?.Dispose();
        _folder = null;
    }

    // ---------------- chạy bản nháp ----------------

    /// <summary>
    /// Chạy bản nháp trên ảnh đang chọn, trên luồng nền (job có thể mất hàng trăm ms). Yêu cầu
    /// đến trong lúc đang chạy được gộp thành MỘT lần chạy lại khi xong.
    /// </summary>
    private async void RunDraft()
    {
        if (_busy)
        {
            _again = true;
            return;
        }
        DraftSession? draft = _draft;
        if (draft is null || _source is null)
        {
            return;
        }
        _busy = true;
        try
        {
            do
            {
                _again = false;
                string code = _code;
                using DisplayFrame input = _source!.Share();   // nguồn có thể bị đổi giữa chừng
                CommandResult? result = await Task.Run(() => draft.Run(code, input));
                if (result is null || IsDisposed)
                {
                    result?.Frame?.Dispose();
                    return;
                }
                ShowDraft(result);
            }
            while (_again && _source is not null);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _draftLine.Text = ex.GetType().Name + ": " + ex.Message;
        }
        finally
        {
            _busy = false;
        }
    }

    private void ShowDraft(CommandResult? result)
    {
        CommandResult? old = _draftResult;
        _draftResult = result;
        if (result is null)
        {
            RecordView.Clear(_display);
            _draftLine.Text = string.Empty;
        }
        else
        {
            RecordView.Show(_display, result.Frame, _draft?.Command(_code)?.Display.Result ??
                                                    string.Empty);
            _draftLine.Text = _text.Text("SetupForm.draft", Verdict(result),
                ResultFrameView.Describe(_text, result), result.Times.RunMs);
            bool same = _running is not null && _running.Status == result.Status &&
                        _running.Outputs.Select(o => o.Text)
                            .SequenceEqual(result.Outputs.Select(o => o.Text));
            _draftLine.ForeColor = _running is null || same ? ForeColor : Color.Firebrick;
        }
        old?.Frame?.Dispose();
    }

    private string Verdict(CommandResult r) => _text.Text("verdict." + r.Status);

    // ---------------- tab Job: tham số nhanh + editor ----------------

    private void BuildTunables(CommandConfig c)
    {
        _tunables.SuspendLayout();
        foreach (Control old in _tunables.Controls.Cast<Control>().ToList())
        {
            old.Dispose();
        }
        if (c.Tunables.Count == 0)
        {
            _tunables.Controls.Add(new Label
            {
                AutoSize = true, MaximumSize = new Size(380, 0),
                Text = _text.Text("SetupForm.noTunables", c.Job),
            });
        }
        foreach (TunableConfig tune in c.Tunables)
        {
            string stepText = Number(tune.Step);
            int dot = stepText.IndexOf('.');
            int decimals = dot < 0 ? 0 : stepText.Length - dot - 1;
            int steps = (int)Math.Round((tune.Max - tune.Min) / tune.Step);
            Label label = new()
            {
                AutoSize = true, Padding = new Padding(0, 8, 0, 0),
                Text = $"{tune.Name} ({tune.Terminal}" +
                       (tune.Unit.Length > 0 ? ", " + tune.Unit : string.Empty) +
                       $")  [{Number(tune.Min)} – {Number(tune.Max)}]",
            };
            NumericUpDown number = new()
            {
                DecimalPlaces = decimals, Minimum = (decimal)tune.Min, Maximum = (decimal)tune.Max,
                Increment = (decimal)tune.Step, Value = (decimal)tune.Value, Width = 110,
            };
            TrackBar bar = new()
            {
                Minimum = 0, Maximum = Math.Max(1, steps), Width = 380,
                TickFrequency = Math.Max(1, steps / 10),
                Value = Math.Min(Math.Max(1, steps),
                    (int)Math.Round((tune.Value - tune.Min) / tune.Step)),
            };
            bool syncing = false;
            void Changed(double value)
            {
                _draft?.SetTunable(_code, tune.Terminal, value);
                _rerun.Stop();
                _rerun.Start();   // kéo thanh trượt: chạy lại một lần khi tay dừng 150 ms
            }
            bar.ValueChanged += (_, _) =>
            {
                if (syncing)
                {
                    return;
                }
                syncing = true;
                double value = Math.Min(tune.Max, tune.Min + bar.Value * tune.Step);
                number.Value = (decimal)value;
                syncing = false;
                Changed(value);
            };
            number.ValueChanged += (_, _) =>
            {
                if (syncing)
                {
                    return;
                }
                syncing = true;
                bar.Value = Math.Min(bar.Maximum,
                    (int)Math.Round(((double)number.Value - tune.Min) / tune.Step));
                syncing = false;
                Changed((double)number.Value);
            };
            _tunables.Controls.Add(label);
            _tunables.Controls.Add(bar);
            _tunables.Controls.Add(number);
        }
        _tunables.ResumeLayout();
    }

    private void OnEditJob(object? sender, EventArgs e)
    {
        if (_draft is null)
        {
            return;
        }
        _draft.EditorOpen = true;   // chạy kèm bỏ lượt: editor sửa ToolBlock không qua khoá job
        try
        {
            JobEditorForm.Edit(this, _text, _draft.Job(_code), _draft.Command(_code)!, _source);
            _draft.MarkJobEdited(_code);
        }
        finally
        {
            _draft.EditorOpen = false;
        }
        RunDraft();
    }

    // ---------------- tab Kết quả & giới hạn ----------------

    private void FillLimits(CommandConfig c)
    {
        _limits.Rows.Clear();
        foreach (OutputConfig o in c.Outputs)
        {
            _limits.Rows.Add(o.Terminal, o.Name, Number(o.Lower), Number(o.Upper),
                Number(o.Offset), o.Format);
        }
    }

    private void OnLimitEdited(object? sender, DataGridViewCellEventArgs e)
    {
        if (_draft is null || e.RowIndex < 0)
        {
            return;
        }
        DataGridViewRow row = _limits.Rows[e.RowIndex];
        string terminal = (string)row.Cells[0].Value;
        try
        {
            double? lower = Parse(row.Cells[2].Value);
            double? upper = Parse(row.Cells[3].Value);
            double offset = Parse(row.Cells[4].Value) ?? 0;
            _draft.SetLimits(_code, terminal, lower, upper, offset);
            RunDraft();
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            MessageBox.Show(this, _text.Text("SetupForm.badLimit", terminal, ex.Message), Text);
            FillLimits(_draft.Command(_code)!);   // trả lại số cũ
        }
    }

    private static double? Parse(object? cell)
    {
        string s = Convert.ToString(cell, CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;
        return s.Length == 0
            ? null
            : double.Parse(s.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    private static string Number(double? value) =>
        value?.ToString("0.#####", CultureInfo.InvariantCulture) ?? string.Empty;

    // ---------------- tab Ảnh & đèn ----------------

    private void FillCapture(CommandConfig c)
    {
        _cameraDefault.Checked = c.ExposureMs is null;
        _exposure.Value = (decimal)Math.Min(1000, Math.Max(0.01, c.ExposureMs ?? 1));
        _exposure.Enabled = c.ExposureMs is not null;
        _lights.Rows.Clear();
        foreach (LightSetting l in c.Lights)
        {
            _lights.Rows.Add(l.Role, l.Channel, l.Intensity);
        }
    }

    private void OnSaveCapture(object? sender, EventArgs e)
    {
        if (_draft is null)
        {
            return;
        }
        try
        {
            List<LightSetting> lights = _lights.Rows.Cast<DataGridViewRow>()
                .Where(r => !r.IsNewRow)
                .Select(r => new LightSetting
                {
                    Role = (string)r.Cells[0].Value,
                    Channel = Convert.ToInt32(r.Cells[1].Value, CultureInfo.InvariantCulture),
                    Intensity = Convert.ToInt32(r.Cells[2].Value, CultureInfo.InvariantCulture),
                }).ToList();
            _draft.SetCapture(_code, _cameraDefault.Checked ? null : (double)_exposure.Value,
                lights);
            UpdateCaptureWarning();
        }
        catch (FormatException ex)
        {
            MessageBox.Show(this, ex.Message, Text);
        }
    }

    private void UpdateCaptureWarning()
    {
        ProductConfig? running = _store.Current;
        _captureWarning.Text = _draft is not null && running is not null &&
                               _draft.CaptureChanged(_code, running)
            ? _text.Text("SetupForm.captureChanged")
            : string.Empty;
    }

    // ---------------- tab Bộ ảnh thử ----------------

    private async void OnRunTestSet(object? sender, EventArgs e)
    {
        DraftSession? draft = _draft;
        if (draft is null || _busy)
        {
            return;
        }
        string folder = _testFolder.Text;
        string code = _code;
        _testResults.Items.Clear();
        _busy = true;
        Cursor = Cursors.WaitCursor;
        try
        {
            List<(string File, CommandResult? Result, Msg? Error)> rows = await Task.Run(() =>
                RunFolder(draft, code, folder));
            int falseAccept = 0, falseReject = 0, labelled = 0;
            foreach ((string file, CommandResult? result, Msg? error) in rows)
            {
                // Chương 24: ảnh trong thư mục con OK\ hoặc NG\ mang nhãn do NGƯỜI gán — ground
                // truth độc lập (Ch16 §16.3.4), khác nhãn do chính phần mềm phân loại.
                string? expected = ExpectedLabel(file);
                bool wrong = false;
                if (expected is not null && result is not null)
                {
                    labelled++;
                    bool gotOk = result.Status == CommandStatus.Ok;
                    falseAccept += expected == "NG" && gotOk ? 1 : 0;
                    falseReject += expected == "OK" && !gotOk ? 1 : 0;
                    wrong = (expected == "OK") != gotOk;
                }
                ListViewItem item = new(Path.GetFileName(file));
                item.SubItems.Add(result is null ? "—" : expected is null ? Verdict(result)
                    : _text.Text("SetupForm.labelled", Verdict(result), expected));
                if (wrong)
                {
                    item.BackColor = Color.Khaki;
                }
                item.SubItems.Add(result is null ? _text.Text(error!) :
                    ResultFrameView.Describe(_text, result));
                item.SubItems.Add(result?.Times.RunMs.ToString(CultureInfo.InvariantCulture) ?? "");
                item.ForeColor = result?.Status switch
                {
                    CommandStatus.Ok => ForeColor,
                    CommandStatus.Ng => Color.Firebrick,
                    _ => Color.DarkOrange,
                };
                _testResults.Items.Add(item);
            }
            _testSummary.Text = _text.Text("SetupForm.testSummary", rows.Count,
                rows.Count(r => r.Result?.Status == CommandStatus.Ok),
                rows.Count(r => r.Result?.Status == CommandStatus.Ng),
                rows.Count(r => r.Result is null || r.Result.Status == CommandStatus.Error)) +
                (labelled == 0 ? string.Empty : Environment.NewLine + _text.Text(
                    "SetupForm.testLabelledSummary", labelled, falseAccept, falseReject));
        }
        finally
        {
            _busy = false;
            Cursor = Cursors.Default;
        }
    }

    /// <summary>
    /// Chạy bản nháp trên mọi ảnh trong thư mục — không record (nhanh), ảnh trả ngay sau mỗi ảnh.
    /// Bản ĐANG CHẠY không chạy ở đây: job của nó thuộc về lệnh của máy; so sánh hai bản trên
    /// sản phẩm thật là việc của chạy kèm.
    /// </summary>
    private static List<(string, CommandResult?, Msg?)> RunFolder(DraftSession draft, string code,
        string folder)
    {
        List<(string, CommandResult?, Msg?)> rows = new();
        // Chương 24: thư mục con OK\ và NG\ là ảnh có nhãn — chạy cả ba, thư mục rỗng thì bỏ qua.
        List<string> folders = new[] { folder, Path.Combine(folder, "OK"),
            Path.Combine(folder, "NG") }.Where(Directory.Exists).ToList();
        foreach (string dir in folders.DefaultIfEmpty(folder))
        {
            RunOneFolder(draft, code, dir, rows, skipEmpty: folders.Count > 1);
        }
        return rows;
    }

    private static void RunOneFolder(DraftSession draft, string code, string folder,
        List<(string, CommandResult?, Msg?)> rows, bool skipEmpty)
    {
        using FolderCamera camera = new("test-set", folder, ImagePatterns, loop: false,
            toGrey: true);
        try
        {
            camera.OpenAsync(CancellationToken.None).GetAwaiter().GetResult();
            while (true)
            {
                CameraImage image = camera.GrabAsync(CaptureSettings.Default,
                    CancellationToken.None).GetAwaiter().GetResult();
                draft.Calibrate(code, image);
                using DisplayFrame frame = new(image, null);
                CommandResult? result = draft.Run(code, frame, withRecord: false);
                result?.Frame?.Dispose();
                // Source của camera đọc thư mục chỉ là TÊN file — ghép lại thư mục để còn nhãn.
                rows.Add((Path.Combine(folder, Path.GetFileName(image.Source)), result, null));
            }
        }
        catch (DeviceException ex) when (ex.Info.Key == "camera.folder.end" ||
                                         (skipEmpty && ex.Info.Key == "camera.folder.empty"))
        {
            // hết ảnh, hoặc thư mục gốc chỉ chứa hai thư mục con OK, NG
        }
        catch (DeviceException ex)
        {
            rows.Add((folder, null, ex.Info));
        }
    }

    /// <summary>Nhãn của ảnh = tên thư mục cha nếu là OK hoặc NG; không thì null.</summary>
    private static string? ExpectedLabel(string file)
    {
        string parent = Path.GetFileName(Path.GetDirectoryName(file) ?? string.Empty);
        return parent.Equals("OK", StringComparison.OrdinalIgnoreCase) ? "OK"
            : parent.Equals("NG", StringComparison.OrdinalIgnoreCase) ? "NG"
            : null;
    }

    // ---------------- chạy lại ảnh đã lưu (Chương 24) ----------------

    private async void OnReplay(object? sender, EventArgs e)
    {
        DraftSession? draft = _draft;
        string version = _store.CurrentVersion;
        if (draft is null || _busy)
        {
            return;
        }
        string code = _code;
        DateTime to = DateTime.Now;
        DateTime from = to.AddDays(-(double)_replayDays.Value);
        _testResults.Items.Clear();
        _busy = true;
        Cursor = Cursors.WaitCursor;
        try
        {
            ReplayReport report = await Task.Run(() =>
                _replay.Run(draft, code, version, from, to, MaxReplayImages));
            _replays[code] = report;
            foreach (ReplayDifference d in report.Differences)
            {
                ListViewItem item = new(d.Recorded.Time.ToString("MM-dd HH:mm:ss",
                    CultureInfo.InvariantCulture) + "  " + d.Recorded.Serial);
                item.SubItems.Add(d.Recorded.Status + " → " +
                                  d.Status.ToString().ToUpperInvariant());
                item.SubItems.Add(string.Join("  ", d.Recorded.Values.Select(v =>
                    v.Name + " " + v.Value + " → " +
                    (d.Outputs.FirstOrDefault(o => o.Name == v.Name)?.Text ?? "-"))));
                item.SubItems.Add(string.Empty);
                item.Tag = d.Recorded.ImageFile;
                item.ForeColor = Color.Firebrick;
                _testResults.Items.Add(item);
            }
            _testSummary.Text = ReplayText(code, report);
            _testSummary.ForeColor = report.Differences.Count > 0 ? Color.Firebrick : ForeColor;
        }
        finally
        {
            _busy = false;
            Cursor = Cursors.Default;
        }
    }

    private const int MaxReplayImages = 2000;

    private string ReplayText(string code, ReplayReport r) =>
        _text.Text("SetupForm.replaySummary", code, r.Images, r.Same, r.Differences.Count,
            r.OtherVersion, r.MissingImages, r.ElapsedMs);

    private static readonly string[] ImagePatterns =
        { "*.bmp", "*.png", "*.jpg", "*.tif", "*.tiff", "*.idb", "*.cdb" };

    // ---------------- chạy kèm, áp dụng, quay lại ----------------

    private void OnShadowCompared(object? sender, ShadowEventArgs e)
    {
        if (IsHandleCreated && !IsDisposed)
        {
            BeginInvoke(new Action(UpdateShadowCounters));
        }
    }

    private void UpdateShadowCounters()
    {
        ShadowCounters c = _shadow.Counters;
        _shadowCounters.Text = _text.Text("SetupForm.shadowCounters", c.Same, c.Different,
            c.Skipped);
        _shadowCounters.ForeColor = c.Different > 0 ? Color.Firebrick : ForeColor;
    }

    private void OnShowDifference(object? sender, EventArgs e)
    {
        IReadOnlyList<ShadowDifference> differences = _shadow.Differences;
        if (differences.Count == 0)
        {
            return;
        }
        ShadowDifference d = differences[_differenceIndex++ % differences.Count];
        if (!string.Equals(d.Running.Code, _code, StringComparison.OrdinalIgnoreCase))
        {
            HighlightCommand(d.Running.Code);
        }
        CommandResult running = d.Running with { Frame = d.Running.Frame!.Share() };
        SetSource(running.Frame, running, "SetupForm.source.difference");
    }

    private void OnChanges(object? sender, EventArgs e)
    {
        string list = ChangesText();
        MessageBox.Show(this, list.Length == 0 ? _text.Text("SetupForm.noChanges") : list, Text);
    }

    private string ChangesText()
    {
        ProductConfig? running = _store.Current;
        return _draft is null || running is null
            ? string.Empty
            : string.Join(Environment.NewLine, _draft.Changes(running).Select(_text.Text));
    }

    private void OnApply(object? sender, EventArgs e)
    {
        if (_draft is null)
        {
            return;
        }
        string changes = ChangesText();
        if (changes.Length == 0)
        {
            MessageBox.Show(this, _text.Text("SetupForm.noChanges"), Text);
            return;
        }
        // Chương 24: kiểm hồi quy đã chạy chưa, kết quả ra sao — hiện trong hộp lý do và vào nhật
        // ký. Không chặn áp dụng: chạy lại ảnh cũ so "khác trước", không so "đúng".
        string replay = ReplayStatus();
        string? reason = ReasonDialog.Ask(this, _text, changes + Environment.NewLine + replay);
        if (reason is null)
        {
            return;
        }
        ApplyResult result = _store.Apply(_draft, reason, _machine());
        // Chương 23: ai, vì sao, đổi gì, từ bản sao lưu nào sang phiên bản nào — một dòng nhật ký.
        _session.Record(result.Ok ? "apply" : "apply-failed", reason, new Dictionary<string, string>
        {
            ["reason"] = reason, ["changes"] = changes,
            ["previousSnapshot"] = result.PreviousSnapshot ?? string.Empty,
            ["version"] = _store.CurrentVersion,
            ["problems"] = result.Problems.Count.ToString(CultureInfo.InvariantCulture),
            ["replay"] = replay,
        });
        _replays.Clear();   // phiên bản đã đổi — kết quả chạy lại cũ không còn áp dụng
        Report(result, "SetupForm.applied");
    }

    /// <summary>Mỗi lệnh đã sửa: đã chạy lại ảnh đã lưu chưa, và ra sao.</summary>
    private string ReplayStatus()
    {
        ProductConfig? running = _store.Current;
        if (_draft is null || running is null)
        {
            return string.Empty;
        }
        IEnumerable<string> changed = running.Commands.Select(c => c.Code)
            .Where(_draft.IsChanged);
        return string.Join(Environment.NewLine, changed.Select(code =>
            _replays.TryGetValue(code, out ReplayReport? r)
                ? ReplayText(code, r)
                : _text.Text("SetupForm.replayNotRun", code)));
    }

    private void OnRollBack(object? sender, EventArgs e)
    {
        if (_draft is null)
        {
            return;
        }
        ProductSnapshot? target = _lastSnapshot is null
            ? _backup.List(_draft.ProductId).FirstOrDefault()
            : _backup.List(_draft.ProductId).FirstOrDefault(s => s.Name == _lastSnapshot);
        if (target is null)
        {
            MessageBox.Show(this, _text.Text("SetupForm.noBackup"), Text);
            return;
        }
        if (MessageBox.Show(this, _text.Text("SetupForm.confirmRollBack", target.Name,
                target.CreatedUtc.ToLocalTime(), target.User, target.Reason), Text,
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }
        ApplyResult result = _store.RollBack(_draft.ProductId, target.Name, _machine(),
            _session.UserName);
        _session.Record(result.Ok ? "rollback" : "rollback-failed", target.Name,
            new Dictionary<string, string>
            {
                ["reason"] = "rollback to " + target.Name, ["restored"] = target.Name,
                ["previousSnapshot"] = result.PreviousSnapshot ?? string.Empty,
                ["version"] = _store.CurrentVersion,
            });
        Report(result, "SetupForm.rolledBack");
    }

    private void Report(ApplyResult result, string okKey)
    {
        if (!result.Ok)
        {
            MessageBox.Show(this, _text.Text("SetupForm.applyFailed") + Environment.NewLine +
                                  string.Join(Environment.NewLine,
                                      result.Problems.Take(10).Select(_text.Text)),
                Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        _lastSnapshot = result.PreviousSnapshot;
        ReopenDraft();   // bản nháp mới = đúng bản vừa chạy
        MessageBox.Show(this, _text.Text(okKey, _lastSnapshot ?? "-"), Text,
            MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void OnDiscard(object? sender, EventArgs e)
    {
        if (MessageBox.Show(this, _text.Text("SetupForm.confirmDiscard"), Text,
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
        {
            ReopenDraft();
        }
    }

    private void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing && ChangesText().Length > 0 &&
            MessageBox.Show(this, _text.Text("SetupForm.confirmClose"), Text,
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            e.Cancel = true;
            return;
        }
        // Gỡ ảnh khỏi khung TRƯỚC khi trả ảnh; dừng chạy kèm trước khi đóng job nháp.
        _rerun.Stop();
        _shadow.Compared -= OnShadowCompared;
        _session.Changed -= OnSessionChanged;
        _text.LanguageChanged -= OnLanguageChanged;
        _shadow.Attach(null);
        RecordView.Clear(_display);
        _draftResult?.Frame?.Dispose();
        _source?.Dispose();
        _running = null;
        foreach (CommandResult r in _recentItems)
        {
            r.Frame?.Dispose();
        }
        CloseFolder();
        _draft?.Dispose();
        _draft = null;
    }

    private void OnSessionChanged(object? sender, EventArgs e)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(ApplyRole));
        }
        else
        {
            ApplyRole();
        }
    }

    private void ApplyRole()
    {
        bool can = _session.Can(Permission.CommandSetup);
        foreach (Control control in _editing)
        {
            control.Enabled = can;
        }
        _locked.Text = can ? string.Empty : _text.Text("SetupForm.locked");
        _locked.Visible = !can;
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        UiText.Apply(this, _text);
        SetColumnHeaders();
        if (_draft is not null && _code.Length > 0)
        {
            UpdateShadowCounters();
            UpdateCaptureWarning();
        }
    }

    /// <summary>Chọn dòng của lệnh trong danh sách (sự kiện chọn tự gọi SelectCommand).</summary>
    private void HighlightCommand(string code)
    {
        for (int i = 0; i < _commands.Items.Count; i++)
        {
            if (((CommandItem)_commands.Items[i]).Code == code)
            {
                _commands.SelectedIndex = i;
            }
        }
    }

    // ---------------- bố cục ----------------

    private void BuildLayout()
    {
        Panel left = new() { Dock = DockStyle.Left, Width = 200, Padding = new Padding(6) };
        _commands.Font = new Font("Segoe UI", 12f);
        _commands.ItemHeight = 30;
        left.Controls.Add(_commands);

        TabControl tabs = new() { Name = "tabs", Dock = DockStyle.Right, Width = 440 };
        TabPage job = new() { Name = "tabJob", Padding = new Padding(8) };
        Button edit = new() { Name = "editJob", Dock = DockStyle.Bottom, Height = 52 };
        edit.Click += OnEditJob;
        job.Controls.Add(_tunables);
        job.Controls.Add(edit);

        TabPage limits = new() { Name = "tabLimits", Padding = new Padding(8) };
        _limits.AllowUserToAddRows = false;
        _limits.AllowUserToDeleteRows = false;
        _limits.RowHeadersVisible = false;
        _limits.RowTemplate.Height = 30;
        _limits.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        foreach (string column in new[] { "colTerminal", "colName", "colLower", "colUpper",
                     "colOffset", "colFormat" })
        {
            _limits.Columns.Add(column, column);
        }
        _limits.Columns[0].ReadOnly = true;
        _limits.Columns[1].ReadOnly = true;
        _limits.Columns[5].ReadOnly = true;
        _limits.CellEndEdit += OnLimitEdited;
        Label limitsNote = new()
        {
            Name = "limitsNote", Dock = DockStyle.Bottom, Height = 44,
        };
        limits.Controls.Add(_limits);
        limits.Controls.Add(limitsNote);

        TabPage capture = new() { Name = "tabCapture", Padding = new Padding(8) };
        FlowLayoutPanel captureFlow = new()
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false,
        };
        _cameraDefault.CheckedChanged += (_, _) => _exposure.Enabled = !_cameraDefault.Checked;
        _lights.AllowUserToAddRows = false;
        _lights.RowHeadersVisible = false;
        _lights.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        foreach (string column in new[] { "colRole", "colChannel", "colIntensity" })
        {
            _lights.Columns.Add(column, column);
        }
        _lights.Columns[0].ReadOnly = true;
        _lights.Columns[1].ReadOnly = true;
        Button saveCapture = new() { Name = "saveCapture", Width = 200, Height = 44 };
        saveCapture.Click += OnSaveCapture;
        Label captureNote = new()
        {
            Name = "captureNote", AutoSize = true, MaximumSize = new Size(400, 0),
        };
        captureFlow.Controls.Add(new Label { Name = "exposureLabel", AutoSize = true });
        captureFlow.Controls.Add(_cameraDefault);
        captureFlow.Controls.Add(_exposure);
        captureFlow.Controls.Add(new Label { Name = "lightsLabel", AutoSize = true });
        captureFlow.Controls.Add(_lights);
        captureFlow.Controls.Add(saveCapture);
        captureFlow.Controls.Add(captureNote);
        capture.Controls.Add(captureFlow);

        TabPage tests = new() { Name = "tabTests", Padding = new Padding(8) };
        FlowLayoutPanel testBar = new() { Dock = DockStyle.Top, Height = 92 };
        Button runTests = new() { Name = "runTests", Width = 100, Height = 38 };
        runTests.Click += OnRunTestSet;
        // Chương 24: chạy lại ảnh máy đã chụp, so với câu trả lời máy đã gửi đi.
        Button replay = new() { Name = "replay", Width = 230, Height = 38 };
        replay.Click += OnReplay;
        testBar.Controls.Add(_testFolder);
        testBar.Controls.Add(runTests);
        testBar.Controls.Add(replay);
        testBar.Controls.Add(new Label
        {
            Name = "replayDaysLabel", AutoSize = true, Padding = new Padding(0, 10, 0, 0),
        });
        testBar.Controls.Add(_replayDays);
        foreach ((string name, int width) in new[] { ("colFile", 140), ("colResult", 110),
                     ("colValues", 130), ("colMs", 40) })
        {
            _testResults.Columns.Add(new ColumnHeader { Name = name, Width = width });
        }
        tests.Controls.Add(_testResults);
        tests.Controls.Add(_testSummary);
        tests.Controls.Add(testBar);

        tabs.TabPages.AddRange(new[] { job, limits, capture, tests });

        Panel center = new() { Dock = DockStyle.Fill, Padding = new Padding(6) };
        FlowLayoutPanel sourceBar = new() { Dock = DockStyle.Top, Height = 44 };
        Button openFolder = new() { Name = "openFolder", Width = 110, Height = 36 };
        openFolder.Click += OnOpenFolder;
        Button next = new() { Name = "nextImage", Width = 80, Height = 36 };
        next.Click += async (_, _) => await NextFolderImage();
        sourceBar.Controls.Add(_recentBox);
        sourceBar.Controls.Add(openFolder);
        sourceBar.Controls.Add(next);
        sourceBar.Controls.Add(_sourceName);
        Panel lines = new() { Dock = DockStyle.Bottom, Height = 52 };
        _draftLine.Font = new Font(Font, FontStyle.Bold);
        lines.Controls.Add(_draftLine);
        lines.Controls.Add(_runningLine);
        _display.Font = Control.DefaultFont;   // control Cognex: font mặc định (spike v2.1)
        center.Controls.Add(_display);
        center.Controls.Add(lines);
        center.Controls.Add(sourceBar);
        center.Controls.Add(_locked);   // dải báo trên cùng, chỉ hiện khi bị khoá

        FlowLayoutPanel bottom = new()
        {
            Dock = DockStyle.Bottom, Height = 64, Padding = new Padding(6, 8, 6, 6),
        };
        Button difference = new() { Name = "showDifference", Width = 140, Height = 46 };
        difference.Click += OnShowDifference;
        Button changes = new() { Name = "changes", Width = 120, Height = 46 };
        changes.Click += OnChanges;
        Button discard = new() { Name = "discard", Width = 130, Height = 46 };
        discard.Click += OnDiscard;
        Button rollBack = new() { Name = "rollBack", Width = 170, Height = 46 };
        rollBack.Click += OnRollBack;
        Button apply = new()
        {
            Name = "apply", Width = 190, Height = 46, Font = new Font(Font, FontStyle.Bold),
        };
        apply.Click += OnApply;
        bottom.Controls.AddRange(new Control[]
        {
            _shadowOn, _shadowCounters, difference, _captureWarning, changes, discard, rollBack,
            apply,
        });
        // Bị đăng xuất (hết giờ) thì khoá phần sửa, KHÔNG đóng form: bản nháp đang dở vẫn còn,
        // kỹ sư đăng nhập lại là làm tiếp (Chương 23, mục 23.2).
        _editing.AddRange(new Control[] { tabs, discard, rollBack, apply, _shadowOn });
        _shadowOn.Padding = new Padding(0, 10, 0, 0);

        Controls.Add(center);
        Controls.Add(tabs);
        Controls.Add(left);
        Controls.Add(bottom);
        SetColumnHeaders();
        HandleCreated += (_, _) =>
        {
            _display.ShowRecordsDropDown = true;   // kỹ sư: chọn record
            RecordView.UseLightBackground(_display.Display);
        };
    }

    private void SetColumnHeaders()
    {
        foreach (DataGridViewColumn column in _limits.Columns.Cast<DataGridViewColumn>()
                     .Concat(_lights.Columns.Cast<DataGridViewColumn>()))
        {
            column.HeaderText = _text.Text("SetupForm." + column.Name);
        }
        foreach (ColumnHeader column in _testResults.Columns)
        {
            column.Text = _text.Text("SetupForm." + column.Name);
        }
    }

    /// <summary>Dòng của danh sách lệnh: hiện "T1 · tên", giữ mã để chọn.</summary>
    private sealed record CommandItem(string Code, string Name)
    {
        public override string ToString() => Code + " · " + Name;
    }
}
