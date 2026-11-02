using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Cognex.VisionPro.Display;
using MeoVision.App.Comm;
using MeoVision.App.Devices;
using MeoVision.Core;
using MeoVision.Core.Devices;
using MeoVision.Core.Localization;
using MeoVision.UI.Display;
using MeoVision.Vision.Devices;

namespace MeoVision.UI.Devices;

/// <summary>
/// Camera &amp; đèn ở cấp máy (mockup v2.2, quyết định D10): trạng thái mọi thiết bị, chụp thử một
/// ảnh hoặc liên tục với phơi sáng / brightness / contrast thử, bật đèn từng kênh. Chụp và bật đèn
/// CHỈ khi OFFLINE: lúc ONLINE camera thuộc về lệnh của máy — chụp tay chen vào làm lệnh chờ làn,
/// và đèn đổi giữa chu kỳ làm ảnh của lệnh sai. Giá trị ở đây là để THỬ; giá trị chạy thật của mỗi
/// lệnh nằm trong Thiết lập lệnh.
/// </summary>
public sealed class DevicesForm : Form
{
    private readonly ILocalizer _text;
    private readonly DeviceManager _devices;
    private readonly CommandService _commands;
    private readonly ListView _list = new()
    {
        Name = "devices", Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true,
        MultiSelect = false, HideSelection = false,
    };
    private readonly CogDisplay _display = new() { Dock = DockStyle.Fill };
    private readonly Label _imageInfo = new() { Dock = DockStyle.Bottom, Height = 24 };
    private readonly Label _offlineOnly = new()
    {
        Dock = DockStyle.Top, Height = 34, BackColor = Color.FromArgb(0xFF, 0xF0, 0xC8),
        ForeColor = Color.FromArgb(0x8F, 0x56, 0x00), TextAlign = ContentAlignment.MiddleLeft,
        Padding = new Padding(8, 0, 0, 0),
    };
    private readonly NumericUpDown _exposure = Number(0.01m, 1000m, 2, 1m);
    private readonly NumericUpDown _brightness = Number(0m, 1m, 3, 0.5m);
    private readonly NumericUpDown _contrast = Number(0m, 1m, 3, 0.5m);
    private readonly CheckBox _useExposure = new() { Name = "useExposure", AutoSize = true };
    private readonly CheckBox _useBrightness = new() { Name = "useBrightness", AutoSize = true };
    private readonly CheckBox _useContrast = new() { Name = "useContrast", AutoSize = true };
    private readonly Button _grab = new() { Name = "grabOne", Width = 140, Height = 40 };
    private readonly Button _live = new() { Name = "live", Width = 140, Height = 40 };
    private readonly NumericUpDown _channel = Number(1m, 16m, 0, 1m);
    private readonly NumericUpDown _intensity = Number(0m, 1023m, 0, 100m);
    private readonly Button _lightOn = new() { Name = "lightOn", Width = 140, Height = 40 };
    private readonly Button _lightsOff = new() { Name = "lightsOff", Width = 140, Height = 40 };
    private readonly Panel _cameraPanel = new() { Dock = DockStyle.Fill };
    private readonly Panel _lightPanel = new() { Dock = DockStyle.Fill, Visible = false };
    private readonly Dictionary<string, Msg> _reasons = new(StringComparer.OrdinalIgnoreCase);
    private readonly Timer _liveTimer = new() { Interval = 100 };
    private bool _grabbing;

    public DevicesForm(ILocalizer text, DeviceManager devices, CommandService commands)
    {
        Guard.ThrowIfNull(text, nameof(text));
        Guard.ThrowIfNull(devices, nameof(devices));
        Guard.ThrowIfNull(commands, nameof(commands));
        _text = text;
        _devices = devices;
        _commands = commands;
        Name = "DevicesForm";
        Font = new Font("Segoe UI", 10f);
        ScreenFit.Apply(this, new Size(1500, 860));

        foreach ((string name, int width) in new[] { ("colRole", 110), ("colKind", 70),
                     ("colStatus", 100), ("colDescription", 260) })
        {
            _list.Columns.Add(new ColumnHeader { Name = name, Width = width });
        }
        _display.Font = Control.DefaultFont;   // control Cognex: font mặc định (spike v2.1)
        RecordView.UseLightBackground(_display);

        BuildCameraPanel();
        BuildLightPanel();
        Panel controls = new() { Dock = DockStyle.Bottom, Height = 170 };
        controls.Controls.Add(_cameraPanel);
        controls.Controls.Add(_lightPanel);
        Panel right = new() { Dock = DockStyle.Fill, Padding = new Padding(6, 0, 0, 0) };
        right.Controls.Add(_display);
        right.Controls.Add(_imageInfo);
        right.Controls.Add(controls);
        SplitContainer split = new() { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1 };
        split.Panel1.Controls.Add(_list);
        split.Panel2.Controls.Add(right);
        Controls.Add(split);
        Controls.Add(_offlineOnly);
        Shown += (_, _) => split.SplitterDistance = 560;

        _list.SelectedIndexChanged += (_, _) => ShowSelected();
        _grab.Click += async (_, _) => await GrabOnce();
        _live.Click += (_, _) => ToggleLive();
        _liveTimer.Tick += async (_, _) => await GrabOnce();
        _lightOn.Click += async (_, _) => await SetLight();
        _lightsOff.Click += async (_, _) => await LightsOff();
        _devices.StatusChanged += OnStatusChanged;
        FormClosed += (_, _) =>
        {
            _liveTimer.Stop();
            _devices.StatusChanged -= OnStatusChanged;
            _display.Image = null;
        };
        UiText.Apply(this, _text);
        Reload();
    }

    private static NumericUpDown Number(decimal min, decimal max, int decimals, decimal value) =>
        new() { Minimum = min, Maximum = max, DecimalPlaces = decimals, Value = value, Width = 90,
                Increment = decimals == 0 ? 1 : 0.1m };

    private void BuildCameraPanel()
    {
        TableLayoutPanel grid = new() { Dock = DockStyle.Fill, ColumnCount = 4, Padding = new Padding(4) };
        void Row(int r, string label, NumericUpDown value, CheckBox use)
        {
            grid.Controls.Add(new Label { Name = label, AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, 0, r);
            grid.Controls.Add(value, 1, r);
            grid.Controls.Add(use, 2, r);
        }
        Row(0, "exposureLabel", _exposure, _useExposure);
        Row(1, "brightnessLabel", _brightness, _useBrightness);
        Row(2, "contrastLabel", _contrast, _useContrast);
        FlowLayoutPanel buttons = new() { AutoSize = true, FlowDirection = FlowDirection.TopDown };
        buttons.Controls.Add(_grab);
        buttons.Controls.Add(_live);
        grid.Controls.Add(buttons, 3, 0);
        grid.SetRowSpan(buttons, 3);
        _cameraPanel.Controls.Add(grid);
    }

    private void BuildLightPanel()
    {
        FlowLayoutPanel flow = new() { Dock = DockStyle.Fill, Padding = new Padding(4) };
        flow.Controls.Add(new Label { Name = "channelLabel", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
        flow.Controls.Add(_channel);
        flow.Controls.Add(new Label { Name = "intensityLabel", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
        flow.Controls.Add(_intensity);
        flow.Controls.Add(_lightOn);
        flow.Controls.Add(_lightsOff);
        _lightPanel.Controls.Add(flow);
    }

    private void Reload()
    {
        string? selected = SelectedRole;
        _list.Items.Clear();
        foreach (string role in _devices.Roles)
        {
            DeviceStatus status = _devices.StatusOf(role);
            ListViewItem item = new(role) { Tag = role };
            item.SubItems.Add(_text.Text(_devices.IsCamera(role) ? "DevicesForm.kindCamera"
                : "DevicesForm.kindLight"));
            item.SubItems.Add(_text.Text("DevicesForm.status." + status));
            item.SubItems.Add(_reasons.TryGetValue(role, out Msg? why) && status != DeviceStatus.Connected
                ? _text.Text(why) : _devices.DescriptionOf(role));
            item.ForeColor = status == DeviceStatus.Connected ? ForeColor : Color.Firebrick;
            item.Selected = role == selected;
            _list.Items.Add(item);
        }
        if (_list.SelectedItems.Count == 0 && _list.Items.Count > 0)
        {
            _list.Items[0].Selected = true;
        }
        ShowSelected();
    }

    private string? SelectedRole => _list.SelectedItems.Count == 0
        ? null
        : (string)_list.SelectedItems[0].Tag;

    private void ShowSelected()
    {
        bool offline = !_commands.Online;
        _offlineOnly.Text = _text.Text("DevicesForm.offlineOnly");
        _offlineOnly.Visible = !offline;
        string? role = SelectedRole;
        bool camera = role is not null && _devices.IsCamera(role);
        _cameraPanel.Visible = camera;
        _lightPanel.Visible = role is not null && !camera;
        foreach (Control c in new Control[] { _grab, _live, _lightOn, _lightsOff })
        {
            c.Enabled = offline;
        }
        if (role is not null && !camera)
        {
            (int channels, int max) = _devices.LightLimits(role);
            _channel.Maximum = Math.Max(1, channels);
            _intensity.Maximum = Math.Max(1, max);
        }
    }

    private void OnStatusChanged(object? sender, DeviceStatusEventArgs e)
    {
        if (IsHandleCreated && !IsDisposed)
        {
            BeginInvoke(new Action(() =>
            {
                _reasons[e.Role] = e.Reason;
                Reload();
            }));
        }
    }

    private async Task GrabOnce()
    {
        string? role = SelectedRole;
        if (_grabbing || role is null || !_devices.IsCamera(role) || _commands.Online)
        {
            _liveTimer.Stop();
            return;
        }
        _grabbing = true;
        Stopwatch watch = Stopwatch.StartNew();
        try
        {
            CaptureSettings settings = new(
                _useExposure.Checked ? (double)_exposure.Value : null,
                _useBrightness.Checked ? (double)_brightness.Value : null,
                _useContrast.Checked ? (double)_contrast.Value : null);
            using CameraImage image = await _devices.GrabAsync(role, settings);
            _display.Image = CogCameraImage.From(image).CopyBase(
                Cognex.VisionPro.CogImageCopyModeConstants.CopyPixels);
            _display.Fit(true);
            _imageInfo.ForeColor = ForeColor;
            _imageInfo.Text = _text.Text("DevicesForm.imageInfo", image.Source, image.Width,
                image.Height, watch.ElapsedMilliseconds);
        }
        catch (DeviceException ex)
        {
            _liveTimer.Stop();
            _imageInfo.ForeColor = Color.Firebrick;
            _imageInfo.Text = _text.Text(ex.Info);
        }
        finally
        {
            _grabbing = false;
            _live.Text = _text.Text(_liveTimer.Enabled ? "DevicesForm.stop" : "DevicesForm.live");
        }
    }

    private void ToggleLive()
    {
        if (_liveTimer.Enabled)
        {
            _liveTimer.Stop();
        }
        else if (!_commands.Online)
        {
            _liveTimer.Start();
        }
        _live.Text = _text.Text(_liveTimer.Enabled ? "DevicesForm.stop" : "DevicesForm.live");
    }

    private async Task SetLight()
    {
        string? role = SelectedRole;
        if (role is null || _commands.Online)
        {
            return;
        }
        try
        {
            await _devices.SetLightsAsync(role, new[]
            {
                new LightChannelSetting((int)_channel.Value, (int)_intensity.Value),
            });
            _imageInfo.ForeColor = ForeColor;
            _imageInfo.Text = _text.Text("DevicesForm.lightSet", role,
                ((int)_channel.Value).ToString(CultureInfo.InvariantCulture),
                ((int)_intensity.Value).ToString(CultureInfo.InvariantCulture));
        }
        catch (DeviceException ex)
        {
            _imageInfo.ForeColor = Color.Firebrick;
            _imageInfo.Text = _text.Text(ex.Info);
        }
    }

    private async Task LightsOff()
    {
        string? role = SelectedRole;
        if (role is null || _commands.Online)
        {
            return;
        }
        try
        {
            await _devices.AllLightsOffAsync(role);
            _imageInfo.Text = _text.Text("DevicesForm.lightsOffDone", role);
        }
        catch (DeviceException ex)
        {
            _imageInfo.ForeColor = Color.Firebrick;
            _imageInfo.Text = _text.Text(ex.Info);
        }
    }
}
