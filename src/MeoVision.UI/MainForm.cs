using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using MeoVision.App;
using MeoVision.App.Alerts;
using MeoVision.App.Commands;
using MeoVision.App.Comm;
using MeoVision.App.Devices;
using MeoVision.App.Hosting;
using MeoVision.App.Products;
using MeoVision.App.Results;
using MeoVision.Core;
using MeoVision.Core.Commands;
using MeoVision.Core.Devices;
using MeoVision.Core.Localization;
using MeoVision.Core.Products;
using MeoVision.UI.Display;
using MeoVision.App.Security;
using MeoVision.Core.Security;
using MeoVision.App.Maintenance;
using MeoVision.UI.History;
using MeoVision.UI.Calibration;
using MeoVision.UI.Comm;
using MeoVision.UI.Devices;
using MeoVision.UI.Maintenance;
using MeoVision.UI.Products;
using MeoVision.UI.Security;
using MeoVision.UI.Settings;
using MeoVision.UI.Setup;

namespace MeoVision.UI;

/// <summary>
/// Cửa sổ chính (Chương 17: khung + đa ngôn ngữ; Chương 22: màn sản xuất). Thanh công cụ lớn cho
/// màn cảm ứng; giữa là lưới khung ảnh — MỘT khung cho mỗi lệnh của sản phẩm đang chạy; dock phải
/// là Nhật ký và Bộ đếm; nút ONLINE/OFFLINE phải nhấn giữ 1 giây để không chạm nhầm. Viết bằng code
/// thay vì Designer để đọc được trọn trong sách; chữ đến từ file ngôn ngữ qua <see cref="UiText"/>.
/// </summary>
public sealed class MainForm : Form
{
    /// <summary>Số dòng nhật ký giữ trên màn — log đầy đủ nằm trong file log.</summary>
    private const int MaxEventRows = 2000;

    private readonly ILocalizer _text;
    private readonly DeviceManager _devices;
    private readonly CommandEngine _engine;
    private readonly ProductStore _store;
    private readonly CommandService _commands;
    private readonly Func<SetupForm> _newSetupForm;
    private readonly AppSettings _settings;
    private readonly ListView _events = new() { Name = "events", ShowItemToolTips = true };
    private readonly ListView _counters = new() { Name = "counters" };
    private readonly ToolStripStatusLabel _state = new() { Name = "state" };
    private readonly ToolStripStatusLabel _productStatus = new() { Name = "productStatus" };
    private readonly ToolStripStatusLabel _deviceSummary = new() { Name = "deviceSummary" };
    private readonly ToolStripStatusLabel _version = new() { Name = "version" };
    private readonly ToolStripComboBox _languageBox = new()
    {
        Name = "language", DropDownStyle = ComboBoxStyle.DropDownList, AutoSize = false,
        Width = 110,
    };
    private readonly ToolStripLabel _product = new() { Name = "productLabel" };
    private readonly ToolStripButton _online = new() { Name = "online" };
    private readonly Timer _holdTimer = new() { Interval = 1000 };
    private readonly TableLayoutPanel _grid = new() { Name = "grid", Dock = DockStyle.Fill };
    private readonly Dictionary<string, ResultFrameView> _views =
        new(StringComparer.OrdinalIgnoreCase);
    // Bản chụp cho luồng lệnh đọc: luồng giao diện dựng bản mới rồi thay cả tham chiếu.
    private volatile IReadOnlyDictionary<string, ResultFrameView> _postTargets =
        new Dictionary<string, ResultFrameView>();
    private readonly Dictionary<string, int[]> _counts = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<EventRow> _rows = new();
    private SetupForm? _setup;
    private HistoryForm? _history;
    private readonly Session _session;
    private readonly UserStore _users;
    private readonly Func<HistoryForm> _newHistoryForm;
    private readonly InputActivityFilter _activity;
    // Thanh nút theo mockup v2.2 (DESIGN_LOG [26]): Tag = tên biểu tượng.
    private readonly ToolStripButton _homeButton = new() { Name = "home", Tag = "home" };
    private readonly ToolStripButton _cameraButton = new() { Name = "camera", Tag = "camera" };
    private readonly ToolStripButton _calibButton = new() { Name = "calib", Tag = "calib" };
    private readonly ToolStripButton _setupButton = new() { Name = "setup", Tag = "job" };
    private readonly ToolStripButton _commButton = new() { Name = "comm", Tag = "comm" };
    private readonly ToolStripButton _productButton = new() { Name = "product", Tag = "product" };
    private readonly ToolStripButton _historyButton = new() { Name = "history", Tag = "history" };
    private readonly ToolStripDropDownButton _settingsButton = new()
    {
        Name = "settings", Tag = "settings", ShowDropDownArrow = false,
    };
    private readonly ToolStripButton _exitButton = new() { Name = "exit", Tag = "exit" };
    private readonly Button _resetCounters = new() { Name = "resetCounters", Height = 44 };
    private readonly CounterStore _counterStore;
    private readonly Func<ProductForm> _newProductForm;
    private readonly Func<DevicesForm> _newDevicesForm;
    private readonly Func<StorageSettingsForm> _newStorageForm;
    private readonly Func<BypassForm> _newBypassForm;
    private readonly Func<CommForm> _newCommForm;
    private readonly Func<CalibrationForm> _newCalibrationForm;
    private readonly AlertFeeder _alertFeeder;
    private readonly AlertBar _alertBar;
    private bool _countsDirty;
    private readonly ToolStripButton _loginButton = new() { Name = "login", Tag = "user" };
    private readonly ToolStripStatusLabel _userStatus = new() { Name = "userStatus" };
    private readonly ToolStripStatusLabel _warning = new() { Name = "warning" };
    private readonly Timer _secondTimer = new() { Interval = 1000 };
    private readonly StationBackup _stationBackup;
    private readonly Func<DiagnosticForm> _newDiagnosticForm;
    private readonly ToolStripMenuItem _diagnosticsItem = new() { Name = "menuDiagnostics" };
    private readonly ToolStripMenuItem _backupItem = new() { Name = "menuBackupStation" };
    private readonly ToolStripMenuItem _storageItem = new() { Name = "menuStorage" };
    private readonly ToolStripMenuItem _bypassItem = new() { Name = "menuBypass" };
    private bool? _canRunProduction;

    public MainForm(ILocalizer text, DeviceManager devices, CommandEngine engine,
        ProductStore store, CommandService commands, Func<SetupForm> newSetupForm,
        AppSettings settings, Session session, UserStore users, Func<HistoryForm> newHistoryForm,
        StationBackup stationBackup, Func<DiagnosticForm> newDiagnosticForm,
        CounterStore counterStore, Func<ProductForm> newProductForm,
        Func<DevicesForm> newDevicesForm, Func<StorageSettingsForm> newStorageForm,
        AlertCenter alerts, AlertFeeder alertFeeder, Func<BypassForm> newBypassForm,
        Func<CommForm> newCommForm, Func<CalibrationForm> newCalibrationForm)
    {
        Guard.ThrowIfNull(newCalibrationForm, nameof(newCalibrationForm));
        _newCalibrationForm = newCalibrationForm;
        Guard.ThrowIfNull(newCommForm, nameof(newCommForm));
        _newCommForm = newCommForm;
        Guard.ThrowIfNull(alerts, nameof(alerts));
        Guard.ThrowIfNull(alertFeeder, nameof(alertFeeder));
        Guard.ThrowIfNull(newBypassForm, nameof(newBypassForm));
        _alertFeeder = alertFeeder;
        _newBypassForm = newBypassForm;
        _alertBar = new AlertBar(text, alerts);
        Guard.ThrowIfNull(counterStore, nameof(counterStore));
        Guard.ThrowIfNull(newProductForm, nameof(newProductForm));
        Guard.ThrowIfNull(newDevicesForm, nameof(newDevicesForm));
        Guard.ThrowIfNull(newStorageForm, nameof(newStorageForm));
        _counterStore = counterStore;
        _newProductForm = newProductForm;
        _newDevicesForm = newDevicesForm;
        _newStorageForm = newStorageForm;
        // Bộ đếm còn từ lần chạy trước (tắt máy giữa ca không mất số của ca).
        foreach (KeyValuePair<string, int[]> pair in counterStore.Load())
        {
            _counts[pair.Key] = pair.Value;
        }
        Guard.ThrowIfNull(text, nameof(text));
        Guard.ThrowIfNull(devices, nameof(devices));
        Guard.ThrowIfNull(engine, nameof(engine));
        Guard.ThrowIfNull(store, nameof(store));
        Guard.ThrowIfNull(commands, nameof(commands));
        Guard.ThrowIfNull(newSetupForm, nameof(newSetupForm));
        Guard.ThrowIfNull(settings, nameof(settings));
        Guard.ThrowIfNull(session, nameof(session));
        Guard.ThrowIfNull(users, nameof(users));
        Guard.ThrowIfNull(newHistoryForm, nameof(newHistoryForm));
        Guard.ThrowIfNull(stationBackup, nameof(stationBackup));
        Guard.ThrowIfNull(newDiagnosticForm, nameof(newDiagnosticForm));
        _stationBackup = stationBackup;
        _newDiagnosticForm = newDiagnosticForm;
        _session = session;
        _users = users;
        _newHistoryForm = newHistoryForm;
        _activity = new InputActivityFilter(session);
        _text = text;
        _devices = devices;
        _engine = engine;
        _store = store;
        _commands = commands;
        _newSetupForm = newSetupForm;
        _settings = settings;
        Name = "MainForm";
        ScreenFit.Maximize(this, settings.FullScreen);

        // Chương 24: bảo trì nằm trong nút Cài đặt — gói chẩn đoán (mọi vai trò), sao lưu (Quản trị).
        _diagnosticsItem.Click += (_, _) => OpenDiagnostics();
        _backupItem.Click += (_, _) => BackupStation();
        _storageItem.Click += (_, _) =>
        {
            using StorageSettingsForm form = _newStorageForm();   // ai cũng xem; chỉ Quản trị lưu
            form.ShowDialog(this);
        };
        _bypassItem.Click += (_, _) =>
        {
            using BypassForm form = _newBypassForm();   // ai cũng xem; Kỹ sư bật/tắt
            form.ShowDialog(this);
        };
        _settingsButton.DropDownItems.Add(_bypassItem);
        _settingsButton.DropDownItems.Add(_storageItem);
        _settingsButton.DropDownItems.Add(_diagnosticsItem);
        _settingsButton.DropDownItems.Add(_backupItem);

        Controls.Add(BuildCenter());
        Controls.Add(_alertBar);   // DESIGN_LOG [28] D3: dưới thanh nút, ẩn khi bình thường
        Controls.Add(BuildToolBar());
        Controls.Add(BuildStatus());

        BuildLanguageBox();
        _text.LanguageChanged += (_, _) => RefreshTexts();
        // Sự kiện đến từ luồng watchdog/luồng lệnh — đổi control phải về luồng giao diện.
        _devices.StatusChanged += (_, e) => OnUi(() => OnDeviceStatus(e));
        _store.CurrentChanged += (_, _) => OnUi(RebuildFrames);
        _engine.Completed += OnCommandCompleted;
        _holdTimer.Tick += (_, _) =>
        {
            _holdTimer.Stop();
            SetOnline(!_commands.Online, "button");
        };
        FormClosing += OnFormClosing;
        // Chương 23: mọi thao tác làm mới đồng hồ tự đăng xuất; mỗi giây hỏi phiên có hết giờ chưa.
        Application.AddMessageFilter(_activity);
        _session.Changed += (_, _) => OnUi(RefreshPermissions);
        _secondTimer.Tick += (_, _) =>
        {
            _session.Tick();
            RefreshUserStatus();
            SaveCountsIfChanged();
            _alertFeeder.Tick(DateTime.Now);   // hết hạn bỏ qua, kênh PLC, lỗi lưu ảnh
        };
        _secondTimer.Start();
        RebuildFrames();
        RefreshTexts();
        Shown += (_, _) => _alertBar.RefreshBar();   // cảnh báo có từ trước khi form hiện
    }

    /// <summary>
    /// Hiện báo cáo khởi động (Chương 17, mục 17.4). Bước then chốt hỏng thì ghi rõ trên thanh
    /// trạng thái rằng trạm KHÔNG được chạy tự động — và trạm vào OFFLINE: máy nhận ERR,OFFLINE
    /// ngay thay vì kết quả từ một trạm thiếu thiết bị/sản phẩm.
    /// </summary>
    public void ShowStartupReport(StartupReport report)
    {
        Guard.ThrowIfNull(report, nameof(report));
        foreach (StepResult step in report.Steps)
        {
            AddEvent(Msg.Of("MainForm.source.startup", Msg.Of(step.NameKey)), step.Status,
                step.ElapsedMs, step.Message);
        }
        _alertFeeder.FromStartup(report);
        _alertBar.RefreshBar();
        _canRunProduction = report.CanRunProduction;
        SetOnline(report.CanRunProduction, "startup");
        RefreshState();
    }

    /// <summary>
    /// Thêm một dòng sự kiện. Lưu Msg chưa dịch để đổi ngôn ngữ thì dịch lại được.
    /// </summary>
    public void AddEvent(Msg source, StepStatus status, long ms, Msg detail,
        Msg? statusText = null)
    {
        EventRow row = new(DateTime.Now, source, status, ms, detail, statusText);
        _rows.Add(row);
        _events.Items.Add(ToItem(row)).EnsureVisible();   // dòng mới nhất luôn nhìn thấy
        if (_rows.Count > MaxEventRows)
        {
            _rows.RemoveAt(0);
            _events.Items.RemoveAt(0);
        }
    }

    // ---------------- khung ảnh theo lệnh ----------------

    /// <summary>Một khung cho mỗi lệnh của sản phẩm đang chạy; đổi sản phẩm thì dựng lại.</summary>
    private void RebuildFrames()
    {
        _grid.SuspendLayout();
        _postTargets = new Dictionary<string, ResultFrameView>();
        foreach (ResultFrameView old in _views.Values.Distinct())
        {
            old.Dispose();   // kết quả còn trên đường tới khung cũ: Post tự trả ảnh
        }
        _views.Clear();
        _grid.Controls.Clear();
        _grid.ColumnStyles.Clear();
        _grid.RowStyles.Clear();
        List<CommandConfig> commands = _store.Current?.Commands ?? new List<CommandConfig>();
        List<List<CommandConfig>> frames = FrameLayout.Group(commands);
        (int columns, int rows) = FrameLayout.Grid(frames.Count, _settings.DisplayColumns);
        _grid.ColumnCount = columns;
        _grid.RowCount = rows;
        for (int i = 0; i < columns; i++)
        {
            _grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / columns));
        }
        for (int i = 0; i < rows; i++)
        {
            _grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / rows));
        }
        for (int i = 0; i < frames.Count; i++)
        {
            ResultFrameView view = new(_text, frames[i], _settings.RecentFramesPerCommand)
            {
                Dock = DockStyle.Fill,
            };
            view.SetupRequested += (_, _) => OpenSetup(view.Code,
                view.Shown is { Frame: not null } shown
                    ? shown with { Frame = shown.Frame.Share() }
                    : null);
            foreach (string code in view.Codes)
            {
                _views[code] = view;
            }
            _grid.Controls.Add(view, i % columns, i / columns);
            // Hàng cuối thiếu khung: khung cuối kéo hết phần còn lại — không để ô trống.
            if (i == frames.Count - 1)
            {
                _grid.SetColumnSpan(view, columns - i % columns);
            }
        }
        _grid.ResumeLayout();
        _postTargets = new Dictionary<string, ResultFrameView>(_views,
            StringComparer.OrdinalIgnoreCase);
        _product.Text = _store.Current is { } p ? p.Id + " — " + p.Name : "—";
        RebuildCounters();
        RefreshProductStatus();
    }

    /// <summary>
    /// Luồng lệnh, ngay sau khi trả lời máy. Việc ở đây phải NGẮN: Share khung cho khung ảnh,
    /// còn lại đẩy sang luồng giao diện.
    /// </summary>
    private void OnCommandCompleted(object? sender, CommandResultEventArgs e)
    {
        CommandResult r = e.Result;
        if (r.Frame is not null && _postTargets.TryGetValue(r.Code, out ResultFrameView? view))
        {
            view.Post(r with { Frame = r.Frame.Share() });
        }
        CommandResult row = r with { Frame = null };
        OnUi(() =>
        {
            Count(row);
            AddEvent(Msg.Of("MainForm.source.command", row.Code),
                row.Status switch
                {
                    CommandStatus.Ok => StepStatus.Ok,
                    CommandStatus.Ng => StepStatus.Warning,
                    CommandStatus.Bypassed => StepStatus.Warning,
                    _ => StepStatus.Failed,
                },
                row.Times.TotalMs,
                // Lỗi không có giá trị: giữ nguyên thông điệp lỗi để còn tra được cách xử lý (D4).
                row.Error is not null && row.Outputs.Count == 0
                    ? row.Error
                    : Msg.Of("MainForm.command.detail", ResultFrameView.Describe(_text, row)),
                Msg.Of("verdict." + row.Status));
        });
    }

    private void OpenSetup(string? code, CommandResult? shown)
    {
        if (!Allowed(Permission.CommandSetup))
        {
            shown?.Frame?.Dispose();
            return;
        }
        if (_setup is null || _setup.IsDisposed)
        {
            _setup = _newSetupForm();
        }
        _setup.OpenFor(code, shown);
    }

    // ---------------- ONLINE / OFFLINE ----------------

    private void SetOnline(bool online, string by)
    {
        _commands.Online = online;
        _session.Record(online ? "online" : "offline", by);
        PaintOnline();
        AddEvent(Msg.Of("MainForm.source.online"), online ? StepStatus.Ok : StepStatus.Warning, 0,
            Msg.Of(online ? "MainForm.online.on" : "MainForm.online.off"));
        RefreshPermissions();   // chữ của nút kèm ổ khoá nếu người đang dùng không được đổi
    }

    // ---------------- bộ đếm ----------------

    private void Count(CommandResult r)
    {
        if (!_counts.TryGetValue(r.Code, out int[]? c))
        {
            c = new int[3];
            _counts[r.Code] = c;
        }
        if (r.Status != CommandStatus.Bypassed)   // bỏ qua đếm riêng ở dải cảnh báo, không lẫn OK/NG/ERR
        {
            c[(int)r.Status]++;
        }
        _countsDirty = true;
        RebuildCounters();
    }

    private void RebuildCounters()
    {
        _counters.BeginUpdate();
        _counters.Items.Clear();
        IEnumerable<string> codes = (_store.Current?.Commands.Select(c => c.Code) ??
                                     Enumerable.Empty<string>()).Union(_counts.Keys,
            StringComparer.OrdinalIgnoreCase);
        foreach (string code in codes)
        {
            int[] c = _counts.TryGetValue(code, out int[]? found) ? found : new int[3];
            int total = c.Sum();
            ListViewItem item = new(code);
            item.SubItems.Add(c[0].ToString(CultureInfo.InvariantCulture));
            item.SubItems.Add(c[1].ToString(CultureInfo.InvariantCulture));
            item.SubItems.Add(c[2].ToString(CultureInfo.InvariantCulture));
            item.SubItems.Add(total == 0 ? "—" : (100.0 * c[0] / total).ToString("0.0",
                CultureInfo.InvariantCulture) + " %");
            _counters.Items.Add(item);
        }
        _counters.EndUpdate();
    }

    // ---------------- dựng giao diện ----------------

    private ToolStrip BuildToolBar()
    {
        // Mockup v2.2: nút 84 px, biểu tượng 34 px trên chữ; khoá = mờ + ổ khoá + nói lý do.
        ToolStrip bar = new()
        {
            Name = "toolbar", GripStyle = ToolStripGripStyle.Hidden, AutoSize = false,
            Height = 84, Font = new Font("Segoe UI", 10f),
            ImageScalingSize = new Size(ToolbarIcons.Size, ToolbarIcons.Size),
            BackColor = Color.FromArgb(0xF7, 0xF8, 0xFA), Padding = new Padding(6, 0, 6, 0),
        };
        ToolStripItem[] left =
        {
            _homeButton, _cameraButton, _calibButton, _setupButton, _commButton, _productButton,
            _historyButton, _settingsButton,
        };
        foreach (ToolStripItem item in left)
        {
            Style(item);
            bar.Items.Add(item);
        }
        bar.Items.Add(new ToolStripSeparator());
        Style(_loginButton);
        Style(_exitButton);
        bar.Items.Add(_loginButton);
        bar.Items.Add(_exitButton);

        _homeButton.Click += (_, _) => Activate();
        _cameraButton.Click += (_, _) => OpenDevices();
        _calibButton.Click += (_, _) =>
        {
            using CalibrationForm form = _newCalibrationForm();   // ai cũng xem; thao tác: Kỹ sư
            form.ShowDialog(this);
        };
        _setupButton.Click += (_, _) => OpenSetup(null, null);
        _commButton.Click += (_, _) =>
        {
            using CommForm form = _newCommForm();   // ai cũng xem; gửi thử: Kỹ sư, khi OFFLINE
            form.ShowDialog(this);
        };
        _productButton.Click += (_, _) => OpenProducts();
        _historyButton.Click += (_, _) => OpenHistory(DateTime.Today, string.Empty);
        _loginButton.Click += (_, _) => LoginForm.ShowDialog(this, _text, _session, _users);
        _exitButton.Click += (_, _) => Close();

        _online.Alignment = ToolStripItemAlignment.Right;
        _online.AutoSize = false;
        _online.Size = new Size(170, 64);
        _online.Font = new Font("Segoe UI", 11f, FontStyle.Bold);
        // Nhấn GIỮ 1 giây: một cú chạm nhầm không đưa máy về OFFLINE giữa ca.
        _online.MouseDown += (_, _) =>
        {
            if (Allowed(Permission.ToggleOnline))
            {
                _holdTimer.Start();
            }
        };
        _online.MouseUp += (_, _) => _holdTimer.Stop();
        _online.MouseLeave += (_, _) => _holdTimer.Stop();
        _languageBox.Alignment = ToolStripItemAlignment.Right;
        _product.Alignment = ToolStripItemAlignment.Right;
        _product.Font = new Font("Segoe UI Semibold", 11.5f);   // 13 pt bị đẩy vào nút tràn ở 125% (đo thật)
        _product.TextAlign = ContentAlignment.MiddleRight;
        // Căn phải: thêm theo thứ tự từ mép phải vào.
        bar.Items.Add(_online);
        bar.Items.Add(_languageBox);
        bar.Items.Add(_product);
        return bar;
    }

    private static void Style(ToolStripItem item)
    {
        item.AutoSize = false;
        item.Size = new Size(96, 78);
        item.TextImageRelation = TextImageRelation.ImageAboveText;
        item.ImageScaling = ToolStripItemImageScaling.None;
        item.Image = ToolbarIcons.Get((string)item.Tag);
    }

    /// <summary>
    /// Rộng tối thiểu 84 px; chữ dài hơn thì nới ra. Đo thật ở 125% (màn 1920 → 1536 px logic): tối thiểu
    /// 96 px làm tên sản phẩm bị đẩy vào nút tràn của thanh công cụ.
    /// </summary>
    private static void FitWidth(ToolStripItem item)
    {
        int text = TextRenderer.MeasureText(item.Text.Replace("&&", "&"), item.Font).Width;
        item.Size = new Size(Math.Max(84, text + 12), 78);
    }

    /// <summary>Nút của form chưa xây trong bản này: hiện, nói rõ, không làm gì.</summary>
    private void NotYet(ToolStripItem item)
    {
        MessageBox.Show(this, _text.Text("MainForm.notYet", item.Text.Replace("&&", "&")), Text,
            MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void OpenProducts()
    {
        using ProductForm form = _newProductForm();
        form.ShowDialog(this);
    }

    private void OpenDevices()
    {
        if (!Allowed(Permission.TestGrab))
        {
            return;
        }
        using DevicesForm form = _newDevicesForm();
        form.ShowDialog(this);
    }

    private void SaveCountsIfChanged()
    {
        if (!_countsDirty)
        {
            return;
        }
        _countsDirty = false;
        try
        {
            _counterStore.Save(_counts);
        }
        catch (IOException)
        {
            _countsDirty = true;   // thử lại ở giây sau
        }
    }

    private void PaintOnline()
    {
        bool on = _commands.Online;
        string lockMark = _session.Can(Permission.ToggleOnline) ? string.Empty : "🔒 ";
        _online.Text = lockMark + _text.Text(on ? "MainForm.online.on" : "MainForm.online.off") +
                       Environment.NewLine +
                       _text.Text(on ? "MainForm.online.onSub" : "MainForm.online.offSub");
        _online.BackColor = on ? Color.FromArgb(0xDC, 0xF2, 0xE3) : Color.FromArgb(0xFF, 0xF0, 0xC8);
        _online.ForeColor = on ? Color.FromArgb(0x16, 0x79, 0x3A) : Color.FromArgb(0x8F, 0x56, 0x00);
        _online.Width = Math.Max(170, TextRenderer.MeasureText(_online.Text, _online.Font).Width + 20);
    }

    private Control BuildCenter()
    {
        SplitContainer split = new() { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel2 };
        _grid.BackColor = Color.FromArgb(240, 240, 240);
        split.Panel1.Controls.Add(_grid);

        TabControl tabs = new() { Name = "dock", Dock = DockStyle.Fill };
        TabPage log = new() { Name = "tabLog" };
        _events.Dock = DockStyle.Fill;
        _events.View = View.Details;
        _events.FullRowSelect = true;
        AddColumn(_events, "colTime", 90);
        AddColumn(_events, "colSource", 120);
        AddColumn(_events, "colStatus", 70);
        AddColumn(_events, "colMs", 50, HorizontalAlignment.Right);
        AddColumn(_events, "colDetail", 600);
        log.Controls.Add(_events);
        TabPage counters = new() { Name = "tabCounters" };
        _counters.Dock = DockStyle.Fill;
        _counters.View = View.Details;
        _counters.Font = new Font("Segoe UI", 12f);
        AddColumn(_counters, "colCode", 80);
        AddColumn(_counters, "colOk", 70, HorizontalAlignment.Right);
        AddColumn(_counters, "colNg", 70, HorizontalAlignment.Right);
        AddColumn(_counters, "colErr", 70, HorizontalAlignment.Right);
        AddColumn(_counters, "colYield", 90, HorizontalAlignment.Right);
        counters.Controls.Add(_counters);
        _resetCounters.Dock = DockStyle.Bottom;
        _resetCounters.Click += (_, _) => ResetCounters();
        counters.Controls.Add(_resetCounters);
        tabs.TabPages.Add(log);
        tabs.TabPages.Add(counters);
        split.Panel2.Controls.Add(tabs);
        // Dock 420 px (spike v2.1: dock rộng làm khung ảnh 2×2 phí chỗ); kéo được. Đặt khi form đã
        // hiện (đã phóng to) — lúc HandleCreated bề rộng còn là kích thước trước khi phóng to, khung
        // ảnh và dock bị chia sai.
        Shown += (_, _) => split.SplitterDistance = Math.Max(200, split.Width - 420);
        return split;
    }

    private StatusStrip BuildStatus()
    {
        StatusStrip status = new() { Name = "status" };
        _version.Spring = true;
        _version.TextAlign = ContentAlignment.MiddleRight;
        status.Items.Add(_state);
        status.Items.Add(_productStatus);
        status.Items.Add(_userStatus);
        status.Items.Add(_warning);
        status.Items.Add(_deviceSummary);
        status.Items.Add(_version);
        return status;
    }

    // ---------------- chữ, trạng thái ----------------

    private void RefreshTexts()
    {
        UiText.Apply(this, _text);
        _version.Text = _text.Text("MainForm.version",
            InstallChecks.AppVersion(GetType().Assembly));
        PaintOnline();
        _changingLanguage = true;
        _languageBox.SelectedIndex = _text.AvailableLanguages.ToList().IndexOf(_text.Language);
        _changingLanguage = false;

        _events.BeginUpdate();
        _events.Items.Clear();
        foreach (EventRow row in _rows)
        {
            _events.Items.Add(ToItem(row));
        }
        _events.EndUpdate();
        RefreshState();
        RefreshDeviceSummary();
        RefreshProductStatus();
        RefreshPermissions();
    }

    // ---------------- người dùng và quyền (Chương 23) ----------------

    /// <summary>
    /// Nút thiếu quyền vẫn HIỆN, có ổ khoá; bấm vào thì nói cần vai trò nào (UI_Spec v2.2 §7.1) —
    /// ẩn đi thì người vận hành không biết chức năng có ở đó và không biết phải gọi ai.
    /// </summary>
    private void RefreshPermissions()
    {
        Mark(_cameraButton, Permission.TestGrab, "MainForm.camera");
        Mark(_calibButton, Permission.ViewHistory, "MainForm.calib");
        Mark(_setupButton, Permission.CommandSetup, "MainForm.setup");
        Mark(_commButton, Permission.ViewHistory, "MainForm.comm");
        Mark(_productButton, Permission.ViewHistory, "MainForm.product");
        Mark(_historyButton, Permission.ViewHistory, "MainForm.historyButton");
        Mark(_settingsButton, Permission.ExportDiagnostics, "MainForm.settings");
        Mark(_exitButton, Permission.ExitApplication, "MainForm.exit");
        Mark(_diagnosticsItem, Permission.ExportDiagnostics, "MainForm.menuDiagnostics");
        Mark(_backupItem, Permission.BackupStation, "MainForm.menuBackupStation");
        Mark(_storageItem, Permission.StationSettings, "MainForm.menuStorage");
        Mark(_bypassItem, Permission.Bypass, "MainForm.menuBypass");
        _homeButton.Text = _text.Text("MainForm.home");
        FitWidth(_homeButton);
        PaintOnline();
        _loginButton.Text = _session.Role == UserRole.Operator
            ? _text.Text("MainForm.login")
            : _text.Text("MainForm.loggedIn", _session.UserName);
        FitWidth(_loginButton);
        if (_warning.Tag is null && _users.Accounts.Any(a => a.IsDefaultPassword))
        {
            _warning.Text = _text.Text("MainForm.defaultPasswords");
            _warning.ForeColor = Color.DarkOrange;
        }
        RefreshUserStatus();
    }

    private void Mark(ToolStripItem item, Permission permission, string key)
    {
        bool can = _session.Can(permission);
        item.Text = _text.Text(key);
        if (item.Tag is string)
        {
            item.Text = item.Text.Replace("&", "&&");   // "Camera & đèn": & là phím tắt của ToolStrip
            FitWidth(item);
        }
        item.ToolTipText = can ? string.Empty : _text.Text("MainForm.needRole",
            _text.Text("role." + Permissions.Required(permission)));
        if (item.Tag is string icon)
        {
            item.Image = ToolbarIcons.Get(icon, locked: !can);   // mờ + ổ khoá, như mockup
        }
        else
        {
            item.Text = (can ? string.Empty : "🔒 ") + item.Text;   // mục trong menu thả xuống
        }
    }

    private void ResetCounters()
    {
        if (!Allowed(Permission.ResetCounters))
        {
            return;
        }
        // Ghi lại số cũ: ai đặt lại, lúc nào, ca trước được bao nhiêu (mockup v2.2).
        _session.Record("counters-reset", string.Empty, _counts.ToDictionary(p => p.Key,
            p => string.Join("/", p.Value), StringComparer.OrdinalIgnoreCase));
        _counts.Clear();
        _countsDirty = true;
        SaveCountsIfChanged();
        RebuildCounters();
    }

    private void RefreshUserStatus()
    {
        TimeSpan? left = _session.RemainingBeforeLogout;
        string role = _text.Text("role." + _session.Role);
        _userStatus.Text = left is null
            ? _text.Text("MainForm.user", _session.UserName, role)
            : _text.Text("MainForm.userTimed", _session.UserName, role,
                left.Value.ToString(@"m\:ss", CultureInfo.InvariantCulture));
    }

    private bool Allowed(Permission permission)
    {
        if (_session.Can(permission))
        {
            return true;
        }
        MessageBox.Show(this, _text.Text("MainForm.needRole",
                _text.Text("role." + Permissions.Required(permission))), Text,
            MessageBoxButtons.OK, MessageBoxIcon.Information);
        return false;
    }

    /// <summary>Cảnh báo cố định trên thanh trạng thái (ví dụ vừa khôi phục quyền Quản trị).</summary>
    public void ShowWarning(Msg message)
    {
        Guard.ThrowIfNull(message, nameof(message));
        _warning.Tag = message;
        _warning.Text = _text.Text(message);
        _warning.ForeColor = Color.White;
        _warning.BackColor = Color.Firebrick;
    }

    // ---------------- bảo trì (Chương 24) ----------------

    private void OpenDiagnostics()
    {
        if (!Allowed(Permission.ExportDiagnostics))
        {
            return;
        }
        using DiagnosticForm form = _newDiagnosticForm();
        form.ShowDialog(this);
    }

    private void BackupStation()
    {
        if (!Allowed(Permission.BackupStation))
        {
            return;
        }
        using SaveFileDialog dialog = new()
        {
            Filter = "Zip (*.zip)|*.zip",
            FileName = "station-" + Environment.MachineName + "-" +
                       DateTime.Now.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture) + ".zip",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }
        StationBackupInfo info = _stationBackup.Create(dialog.FileName, _session.UserName,
            InstallChecks.AppVersion(typeof(MainForm).Assembly));
        _session.Record("station-backup", dialog.FileName, new Dictionary<string, string>
        {
            ["files"] = info.Files.Count.ToString(CultureInfo.InvariantCulture),
        });
        MessageBox.Show(this, _text.Text("MainForm.backupDone", dialog.FileName, info.Files.Count),
            Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void OpenHistory(DateTime day, string serial)
    {
        if (_history is null || _history.IsDisposed)
        {
            _history = _newHistoryForm();
        }
        _history.ShowFor(day, serial);
    }

    private void RefreshProductStatus()
    {
        _productStatus.Text = _text.Text("MainForm.productStatus", _store.Current?.Id ?? "—");
    }

    private void OnDeviceStatus(DeviceStatusEventArgs e)
    {
        AddEvent(Msg.Of("MainForm.source.device", e.Role),
            e.Status == DeviceStatus.Connected ? StepStatus.Ok : StepStatus.Failed, 0, e.Reason);
        RefreshDeviceSummary();
    }

    private void RefreshDeviceSummary()
    {
        int connected = _devices.Roles.Count(r => _devices.StatusOf(r) == DeviceStatus.Connected);
        _deviceSummary.Text = _text.Text("MainForm.deviceSummary", connected, _devices.Roles.Count);
        _deviceSummary.ForeColor = connected == _devices.Roles.Count
            ? Color.DarkGreen : Color.Firebrick;
    }

    /// <summary>
    /// Chụp thử mọi camera một lần (Chương 18, mục 18.8). Chỉ khi OFFLINE: khi ONLINE camera thuộc
    /// về lệnh của máy (UI_Spec v2.2 §7.2).
    /// </summary>
    private void RefreshState()
    {
        if (_canRunProduction is null)
        {
            return;
        }
        _state.Text = _text.Text(_canRunProduction.Value
            ? "MainForm.state.ready" : "MainForm.state.blocked");
        _state.ForeColor = _canRunProduction.Value ? Color.DarkGreen : Color.Firebrick;
    }

    private ListViewItem ToItem(EventRow row)
    {
        ListViewItem item = new(row.Time.ToString("HH:mm:ss.fff"))
        {
            ForeColor = row.Status switch
            {
                StepStatus.Ok => Color.Black,
                StepStatus.Warning => Color.DarkOrange,
                _ => Color.Firebrick,
            },
        };
        item.SubItems.Add(_text.Text(row.Source));
        item.SubItems.Add(row.StatusText is null
            ? _text.Text("status." + row.Status)
            : _text.Text(row.StatusText));
        item.SubItems.Add(row.Ms.ToString());
        item.SubItems.Add(_text.Text(row.Detail));
        item.ToolTipText = AlertBar.Hint(_text, row.Detail);   // D4: cách xử lý khi rê chuột
        return item;
    }

    private bool _changingLanguage;

    private void BuildLanguageBox()
    {
        // Tên ngôn ngữ viết bằng chính ngôn ngữ đó: người không đọc được ngôn ngữ đang hiển thị
        // vẫn tìm được đường về ngôn ngữ của mình.
        foreach (string language in _text.AvailableLanguages)
        {
            _languageBox.Items.Add(_text.DisplayName(language));
        }
        _languageBox.SelectedIndexChanged += (_, _) =>
        {
            if (!_changingLanguage && _languageBox.SelectedIndex >= 0)
            {
                _text.SetLanguage(_text.AvailableLanguages.ElementAt(_languageBox.SelectedIndex));
            }
        };
    }

    private static void AddColumn(ListView list, string name, int width,
        HorizontalAlignment align = HorizontalAlignment.Left)
    {
        list.Columns.Add(new ColumnHeader { Name = name, Width = width, TextAlign = align });
    }

    private void OnUi(Action action)
    {
        if (IsHandleCreated && !IsDisposed)
        {
            BeginInvoke(action);
        }
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        // Đóng phần mềm vision khi máy đang chạy = máy mất "mắt" giữa chu kỳ. Chỉ hỏi khi
        // người dùng tự bấm đóng; "End task"/tắt Windows thì không hỏi (CloseReason khác
        // UserClosing).
        // Chương 23: Vận hành không được tắt phần mềm (máy mất "mắt"); tắt Windows thì vẫn cho.
        if (e.CloseReason == CloseReason.UserClosing && !Allowed(Permission.ExitApplication))
        {
            e.Cancel = true;
            return;
        }
        if (e.CloseReason == CloseReason.UserClosing &&
            MessageBox.Show(_text.Text("MainForm.confirmClose"), Text, MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            e.Cancel = true;
            return;
        }
        _engine.Completed -= OnCommandCompleted;
        _secondTimer.Stop();
        SaveCountsIfChanged();
        Application.RemoveMessageFilter(_activity);
        _history?.Close();
        _postTargets = new Dictionary<string, ResultFrameView>();
        _setup?.Close();
        foreach (ResultFrameView view in _views.Values.Distinct())
        {
            view.Dispose();   // trả ảnh đang giữ trước khi đóng thiết bị
        }
        _views.Clear();
    }

    private sealed record EventRow(DateTime Time, Msg Source, StepStatus Status, long Ms,
        Msg Detail, Msg? StatusText);
}
