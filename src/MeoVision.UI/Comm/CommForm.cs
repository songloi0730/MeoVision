using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using MeoVision.App.Comm;
using MeoVision.App.Comm.Plc;
using MeoVision.App.Products;
using MeoVision.App.Security;
using MeoVision.Core;
using MeoVision.Core.Localization;
using MeoVision.Core.Security;

namespace MeoVision.UI.Comm;

/// <summary>
/// Giao tiếp (DESIGN_LOG [28] D2): mỗi kênh đang ở trạng thái nào, máy gửi gì và vision trả gì (chuỗi thô +
/// thời gian), bit bắt tay PLC trực tiếp, và gửi thử một dòng khi OFFLINE. Học từ RefVis-C (cây server, gửi/
/// nhận), RefVis-D, RefVis-H (form giao tiếp PLC) — nghiệm thu với PLC mà không thấy chuỗi thô là đoán mò.
/// </summary>
public sealed class CommForm : Form
{
    private static readonly string[] BitNames =
        { "Trigger", "TriggerAck", "Complete", "ResultsValid", "Online", "Error", "Heartbeat", "Bypass" };

    private readonly ILocalizer _text;
    private readonly ChannelHost _host;
    private readonly CommandService _service;
    private readonly ProductStore _store;
    private readonly Session _session;
    private readonly ListView _channels = new()
    {
        Name = "channels", Dock = DockStyle.Top, Height = 150, View = View.Details, FullRowSelect = true,
        MultiSelect = false, HideSelection = false,
    };
    private readonly Label _bits = new()
    {
        Dock = DockStyle.Top, Height = 34, Font = new Font("Consolas", 10.5f),
        TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(8, 0, 0, 0),
    };
    private readonly ListView _log = new()
    {
        Name = "log", Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true,
        Font = new Font("Consolas", 9.5f),
    };
    private readonly CheckBox _hideHeartbeat = new() { Name = "hideHeartbeat", AutoSize = true, Checked = true };
    private readonly ComboBox _testChannel = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
    private readonly TextBox _testLine = new() { Name = "testLine", Width = 360 };
    private readonly Button _send = new() { Name = "send", Width = 130, Height = 36 };
    private readonly Label _testReply = new() { AutoSize = true, Padding = new Padding(8, 8, 0, 0) };
    private readonly Timer _timer = new() { Interval = 500 };
    private long _shown;

    public CommForm(ILocalizer text, ChannelHost host, CommandService service, ProductStore store,
        Session session)
    {
        Guard.ThrowIfNull(text, nameof(text));
        Guard.ThrowIfNull(host, nameof(host));
        Guard.ThrowIfNull(service, nameof(service));
        Guard.ThrowIfNull(store, nameof(store));
        Guard.ThrowIfNull(session, nameof(session));
        _text = text;
        _host = host;
        _service = service;
        _store = store;
        _session = session;
        Name = "CommForm";
        Font = new Font("Segoe UI", 10f);
        ScreenFit.Apply(this, new Size(1300, 800));

        foreach ((string name, int width) in new[] { ("colId", 80), ("colDescription", 300),
                     ("colConnections", 110), ("colLast", 180) })
        {
            _channels.Columns.Add(new ColumnHeader { Name = name, Width = width });
        }
        foreach ((string name, int width) in new[] { ("colTime", 110), ("colChannel", 70),
                     ("colRequest", 330), ("colReply", 380), ("colReason", 130), ("colMs", 60),
                     ("colTest", 60) })
        {
            _log.Columns.Add(new ColumnHeader { Name = name, Width = width });
        }
        // Tự cao theo nội dung: ở 125% chiều cao cố định cắt mất hàng nút cuối (đo thật).
        FlowLayoutPanel test = new()
        {
            Dock = DockStyle.Bottom, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(6),
        };
        test.Controls.Add(new Label { Name = "testLabel", AutoSize = true, Padding = new Padding(0, 8, 0, 0) });
        test.Controls.Add(_testChannel);
        test.Controls.Add(_testLine);
        test.Controls.Add(_send);
        test.Controls.Add(_testReply);
        test.SetFlowBreak(_testReply, true);
        Button clear = new() { Name = "clear", Width = 130, Height = 36 };
        Button close = new() { Name = "close", Width = 110, Height = 36, DialogResult = DialogResult.Cancel };
        test.Controls.Add(_hideHeartbeat);
        test.Controls.Add(clear);
        test.Controls.Add(close);
        Controls.Add(_log);
        Controls.Add(_bits);
        Controls.Add(_channels);
        Controls.Add(test);
        CancelButton = close;

        foreach (IChannel c in _host.Channels)
        {
            _testChannel.Items.Add(c.Id);
        }
        if (_testChannel.Items.Count > 0)
        {
            _testChannel.SelectedIndex = 0;
        }
        string? firstCode = _store.Current?.Commands.FirstOrDefault()?.Code;
        _testLine.Text = firstCode is null ? "HB" : $"{firstCode},{_store.Current!.Id},TEST-1";
        _send.Click += async (_, _) => await SendTest();
        clear.Click += (_, _) =>
        {
            _service.Log.Clear();
            _log.Items.Clear();
            _shown = 0;
        };
        _hideHeartbeat.CheckedChanged += (_, _) =>
        {
            _log.Items.Clear();
            _shown = 0;
            RefreshLog();
        };
        _timer.Tick += (_, _) => RefreshAll();
        FormClosed += (_, _) => _timer.Stop();
        UiText.Apply(this, _text);
        RefreshAll();
        _timer.Start();
    }

    private void RefreshAll()
    {
        RefreshChannels();
        RefreshLog();
        bool canTest = !_service.Online && _session.Can(Permission.TestGrab);
        _send.Enabled = canTest;
        if (!canTest && _testReply.Tag is null)
        {
            _testReply.ForeColor = Color.DimGray;
            _testReply.Text = _service.Online
                ? _text.Text("CommForm.offlineOnly")
                : _text.Text("MainForm.needRole", _text.Text("role." + Permissions.Required(Permission.TestGrab)));
        }
    }

    private void RefreshChannels()
    {
        string? selected = _channels.SelectedItems.Count > 0 ? (string)_channels.SelectedItems[0].Tag : null;
        // Cập nhật tại chỗ, không xoá rồi vẽ lại mỗi nửa giây — danh sách nhấp nháy (thấy khi chụp).
        IReadOnlyList<IChannel> channels = _host.Channels;
        if (_channels.Items.Count != channels.Count)
        {
            _channels.Items.Clear();
            foreach (IChannel c in channels)
            {
                ListViewItem item = new(c.Id) { Tag = c.Id };
                item.SubItems.Add(string.Empty);
                item.SubItems.Add(string.Empty);
                item.SubItems.Add(string.Empty);
                _channels.Items.Add(item);
            }
        }
        for (int i = 0; i < channels.Count; i++)
        {
            IChannel c = channels[i];
            ListViewItem item = _channels.Items[i];
            Set(item, 1, c.Description);
            Set(item, 2, c.Connections.ToString(CultureInfo.InvariantCulture));
            Set(item, 3, c.LastMessageUtc is { } last
                ? _text.Text("CommForm.ago", (DateTime.UtcNow - last).TotalSeconds)
                : "—");
            Color color = c is PlcChannel && c.Connections == 0 ? Color.Firebrick : ForeColor;
            if (item.ForeColor != color)
            {
                item.ForeColor = color;
            }
        }

        IChannel? shown = _host.Channels.FirstOrDefault(c => c.Id == selected) ??
                          _host.Channels.OfType<PlcChannel>().FirstOrDefault();
        if (shown is PlcChannel plc)
        {
            IReadOnlyList<bool> bits = plc.LastBits;
            _bits.Text = bits.Count == 0
                ? plc.Id + ": " + _text.Text("CommForm.plcNotConnected")
                : plc.Id + ":  " + string.Join("   ", BitNames.Take(bits.Count)
                    .Select((n, i) => n + (bits[i] ? " ■" : " □")));
        }
        else
        {
            _bits.Text = _text.Text("CommForm.bitsHint");
        }
    }

    private static void Set(ListViewItem item, int column, string text)
    {
        if (item.SubItems[column].Text != text)
        {
            item.SubItems[column].Text = text;
        }
    }

    private void RefreshLog()
    {
        IReadOnlyList<CommEntry> fresh = _service.Log.Since(_shown);
        if (fresh.Count == 0)
        {
            return;
        }
        _log.BeginUpdate();
        foreach (CommEntry e in fresh)
        {
            _shown = e.Index;
            if (_hideHeartbeat.Checked && e.Request.StartsWith(CommandService.Heartbeat, StringComparison.Ordinal))
            {
                continue;
            }
            ListViewItem item = new(e.Time.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture));
            item.SubItems.Add(e.Channel);
            item.SubItems.Add(e.Request);
            item.SubItems.Add(e.Reply);
            item.SubItems.Add(e.Reason);
            item.SubItems.Add(e.Ms.ToString(CultureInfo.InvariantCulture));
            item.SubItems.Add(e.Test ? "✓" : "");
            string status = e.Reply.Split(',').Skip(2).FirstOrDefault() ?? string.Empty;
            item.ForeColor = status switch
            {
                MessageProtocol.Ng => Color.Firebrick,
                MessageProtocol.Err => Color.DarkOrange,
                MessageProtocol.Bypass => Color.DarkGoldenrod,
                _ => ForeColor,
            };
            _log.Items.Add(item);
        }
        while (_log.Items.Count > CommLog.Capacity)
        {
            _log.Items.RemoveAt(0);
        }
        _log.EndUpdate();
        if (_log.Items.Count > 0)
        {
            _log.Items[_log.Items.Count - 1].EnsureVisible();
        }
    }

    private async Task SendTest()
    {
        if (_testChannel.SelectedItem is not string channel || _service.Online ||
            !_session.Can(Permission.TestGrab))
        {
            return;
        }
        _send.Enabled = false;
        _testReply.Tag = "sent";
        _testReply.ForeColor = ForeColor;
        _testReply.Text = "…";
        try
        {
            string reply = await _service.TestAsync(channel, _testLine.Text.Trim());
            _testReply.Text = _text.Text("CommForm.reply", reply);
        }
        catch (InvalidOperationException)
        {
            _testReply.Text = _text.Text("CommForm.offlineOnly");   // vừa chuyển ONLINE
        }
        finally
        {
            _send.Enabled = !_service.Online;
        }
    }
}
