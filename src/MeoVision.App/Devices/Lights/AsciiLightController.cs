using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MeoVision.Core;
using MeoVision.Core.Devices;
using MeoVision.Core.Localization;

namespace MeoVision.App.Devices.Lights;

/// <summary>
/// Giao thức đèn dạng dòng ASCII, mô tả bằng MẪU LỆNH trong <c>machine.json</c> thay vì viết cứng
/// cho một hãng (Chương 18, mục 18.6). <c>{channel}</c> và <c>{value}</c> (có thể kèm định dạng,
/// ví dụ <c>{value:000}</c>) được thay khi gửi. Giao thức nhị phân thì viết một lớp riêng cài
/// <see cref="ILightController"/> — loader và DeviceManager không phải đổi gì.
/// </summary>
public sealed class AsciiLightController : ILightController, IHealthCheck
{
    private readonly ILineChannel _channel;
    private readonly AsciiLightProtocol _protocol;
    private readonly int?[] _lastSent;

    public AsciiLightController(string role, string description, ILineChannel channel,
        AsciiLightProtocol protocol)
    {
        Guard.ThrowIfNull(role, nameof(role));
        Guard.ThrowIfNull(channel, nameof(channel));
        Guard.ThrowIfNull(protocol, nameof(protocol));
        Role = role;
        Description = description;
        _channel = channel;
        _protocol = protocol;
        _lastSent = new int?[protocol.ChannelNames.Count];
    }

    public string Role { get; }

    public string Description { get; }

    public int ChannelCount => _protocol.ChannelNames.Count;

    public int MaxIntensity => _protocol.MaxIntensity;

    public Task OpenAsync(CancellationToken ct)
    {
        _channel.Open();
        Array.Clear(_lastSent, 0, _lastSent.Length);   // không biết đèn đang ở mức nào
        return Task.CompletedTask;
    }

    public void Close() => _channel.Close();

    public async Task SetAsync(IReadOnlyList<LightChannelSetting> channels, CancellationToken ct)
    {
        Guard.ThrowIfNull(channels, nameof(channels));
        foreach (LightChannelSetting setting in channels)
        {
            if (setting.Channel < 1 || setting.Channel > ChannelCount)
            {
                throw new DeviceException(Msg.Of("light.badChannel", Role, setting.Channel,
                    ChannelCount));
            }
            if (setting.Intensity < 0 || setting.Intensity > _protocol.MaxIntensity)
            {
                throw new DeviceException(Msg.Of("light.badIntensity", Role, setting.Intensity,
                    _protocol.MaxIntensity));
            }

            int index = setting.Channel - 1;
            if (_lastSent[index] == setting.Intensity)
            {
                continue;   // kênh đã ở đúng mức: bỏ qua, tiết kiệm vài ms mỗi chu kỳ
            }
            _lastSent[index] = null;   // gửi dở mà lỗi thì coi như không biết mức hiện tại
            string name = _protocol.ChannelNames[index];
            await SendAsync(_protocol.SetCommand, name, setting.Intensity, ct)
                .ConfigureAwait(false);
            _lastSent[index] = setting.Intensity;
        }
    }

    public async Task CheckAsync(CancellationToken ct)
    {
        if (_protocol.QueryCommand.Length > 0)
        {
            await _channel.SendAsync(_protocol.QueryCommand + _protocol.Terminator, ct)
                .ConfigureAwait(false);
            await _channel.ReadUntilAsync(_protocol.ReplyTerminator, ct).ConfigureAwait(false);
        }
    }

    public void Dispose() => _channel.Dispose();

    private async Task SendAsync(string template, string channel, int value, CancellationToken ct)
    {
        string command = AsciiLightProtocol.Format(template, channel, value);
        await _channel.SendAsync(command + _protocol.Terminator, ct).ConfigureAwait(false);
        if (_protocol.Reply.Length == 0)
        {
            return;   // bộ điều khiển không trả lời — chỉ biết đã GỬI, không biết đã NHẬN
        }

        string expected = AsciiLightProtocol.Format(_protocol.Reply, channel, value);
        string actual = await _channel.ReadUntilAsync(_protocol.ReplyTerminator, ct)
            .ConfigureAwait(false);
        if (!string.Equals(actual.Trim(), expected, StringComparison.Ordinal))
        {
            throw new DeviceException(Msg.Of("light.badReply", Role, command, expected, actual));
        }
    }
}

/// <summary>Mô tả giao thức đọc từ <c>settings</c> của đèn trong <c>machine.json</c>.</summary>
public sealed class AsciiLightProtocol
{
    public IReadOnlyList<string> ChannelNames { get; set; } = Array.Empty<string>();
    public int MaxIntensity { get; set; } = 255;
    public string SetCommand { get; set; } = string.Empty;
    public string Terminator { get; set; } = string.Empty;
    public string Reply { get; set; } = string.Empty;
    public string ReplyTerminator { get; set; } = "\r\n";
    public string QueryCommand { get; set; } = string.Empty;

    /// <summary>Thay {channel}/{value} — chấp nhận định dạng số kiểu {value:000}.</summary>
    public static string Format(string template, string channel, int value) =>
        string.Format(CultureInfo.InvariantCulture,
            template.Replace("{channel", "{0").Replace("{value", "{1"), channel, value);
}

/// <summary>Factory cho loại "AsciiLight" (Chương 18, mục 18.6).</summary>
public sealed class AsciiLightFactory : IDeviceFactory
{
    public DeviceKind Kind => DeviceKind.Light;

    public string Type => "AsciiLight";

    public IReadOnlyList<Msg> Validate(DeviceConfig config)
    {
        List<Msg> problems = new();
        Read(config, problems);
        return problems;
    }

    public IDevice Create(DeviceConfig config, string configDirectory)
    {
        List<Msg> problems = new();
        (ILineChannel channel, string description, AsciiLightProtocol protocol) =
            Read(config, problems);
        if (problems.Count > 0)
        {
            throw new DeviceException(problems[0]);
        }
        return new AsciiLightController(config.Role, description, channel, protocol);
    }

    private static (ILineChannel, string, AsciiLightProtocol) Read(DeviceConfig config,
        List<Msg> problems)
    {
        SettingsReader s = new(config, problems);
        int count = s.Int("channels", 4, 1, 32);
        string names = s.Optional("channelNames", string.Empty);
        AsciiLightProtocol protocol = new()
        {
            // Mặc định kênh tên "1".."n"; bộ điều khiển dùng A/B/C... thì khai channelNames.
            ChannelNames = names.Length > 0
                ? names.Split(',').Select(n => n.Trim()).ToList()
                : Enumerable.Range(1, count).Select(i => i.ToString(CultureInfo.InvariantCulture))
                    .ToList(),
            MaxIntensity = s.Int("maxIntensity", 255, 1, 65535),
            SetCommand = s.Required("setCommand"),
            Terminator = s.Optional("terminator", string.Empty),
            Reply = s.Optional("reply", string.Empty),
            ReplyTerminator = s.Optional("replyTerminator", "\r\n"),
            QueryCommand = s.Optional("queryCommand", string.Empty),
        };
        if (protocol.SetCommand.Length > 0)
        {
            CheckTemplate(config.Role, "setCommand", protocol.SetCommand, problems);
            CheckTemplate(config.Role, "reply", protocol.Reply, problems);
        }

        string transport = s.Optional("transport", "Serial");
        if (string.Equals(transport, "Tcp", StringComparison.OrdinalIgnoreCase))
        {
            string host = s.Required("host");
            int port = s.Int("port", 0, 1, 65535);
            return (new TcpLineChannel(host, port), $"TCP {host}:{port}", protocol);
        }
        if (!string.Equals(transport, "Serial", StringComparison.OrdinalIgnoreCase))
        {
            problems.Add(Msg.Of("machine.setting.badValue", config.Role, "transport", transport,
                "Serial, Tcp"));
        }
        string portName = s.Required("port");
        int baud = s.Int("baudRate", 9600, 1200, 921600);
        return (new SerialLineChannel(portName, baud), $"{portName} {baud}", protocol);
    }

    private static void CheckTemplate(string role, string key, string template,
        List<Msg> problems)
    {
        try
        {
            // Thử định dạng ngay lúc nạp cấu hình: mẫu sai lộ ra khi mở máy, không phải lúc chụp.
            AsciiLightProtocol.Format(template, "1", 0);
        }
        catch (FormatException)
        {
            problems.Add(Msg.Of("machine.setting.badTemplate", role, key, template));
        }
    }
}
