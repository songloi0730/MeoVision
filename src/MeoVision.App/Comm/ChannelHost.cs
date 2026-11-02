using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using MeoVision.App.Comm.Plc;
using MeoVision.App.Hosting;
using MeoVision.Core;
using MeoVision.Core.Devices;
using MeoVision.Core.Localization;
using Microsoft.Extensions.Logging;

namespace MeoVision.App.Comm;

/// <summary>
/// Dựng và mở mọi kênh khai trong <c>machine.json → channels</c> (Chương 21, mục 21.6). Mỗi kiểu
/// kênh một nhánh; mọi kênh dùng chung một <see cref="CommandService"/>.
/// </summary>
public sealed class ChannelHost : IDisposable
{
    public const string TcpServer = "TcpServer";
    public const string McPlc = "McPlc";

    private readonly CommandService _service;
    private readonly ILoggerFactory _loggers;
    private readonly ILogger<ChannelHost> _logger;
    private readonly List<IChannel> _channels = new();

    public ChannelHost(CommandService service, ILoggerFactory loggers)
    {
        Guard.ThrowIfNull(service, nameof(service));
        Guard.ThrowIfNull(loggers, nameof(loggers));
        _service = service;
        _loggers = loggers;
        _logger = loggers.CreateLogger<ChannelHost>();
    }

    public IReadOnlyList<IChannel> Channels => _channels;

    /// <summary>Kiểm thiết lập của một kênh theo kiểu — gọi lúc nạp <c>machine.json</c>.</summary>
    public static void Validate(ChannelConfig channel, List<Msg> problems)
    {
        Guard.ThrowIfNull(channel, nameof(channel));
        switch (channel.Type)
        {
            case TcpServer:
                TcpChannelOptions.Read(channel, problems);
                break;
            case McPlc:
                PlcChannelOptions.Read(channel, problems);
                break;
            default:
                problems.Add(Msg.Of("machine.channel.unknownType", channel.Id, channel.Type,
                    TcpServer + ", " + McPlc));
                break;
        }
    }

    /// <summary>
    /// Mở mọi kênh. Kênh TCP không mở được cổng (đang bị chiếm) là lỗi báo ngay; kênh PLC tự nối
    /// lại trong nền nên luôn "mở được" — trạng thái nối thể hiện qua <see
    /// cref="IChannel.Connections"/>.
    /// </summary>
    public async Task<StepOutcome> StartAsync(MachineConfig machine, CancellationToken ct)
    {
        Guard.ThrowIfNull(machine, nameof(machine));
        List<Msg> failures = new();
        foreach (ChannelConfig config in machine.Channels)
        {
            List<Msg> problems = new();
            IChannel channel = config.Type == McPlc
                ? new PlcChannel(config.Id, PlcChannelOptions.Read(config, problems),
                    _service.HandleAsync, () => _service.Online,
                    _loggers.CreateLogger<PlcChannel>())
                : new TcpChannel(config.Id, TcpChannelOptions.Read(config, problems),
                    _service.HandleAsync, _loggers.CreateLogger<TcpChannel>());
            try
            {
                await channel.StartAsync(ct).ConfigureAwait(false);
                _channels.Add(channel);
            }
            catch (SocketException ex)
            {
                _logger.LogError(ex, "Channel {Channel} could not start ({Endpoint})", config.Id,
                    channel.Description);
                failures.Add(Msg.Of("channel.startFailed", config.Id, channel.Description,
                    ex.Message));
                channel.Dispose();
            }
        }
        return failures.Count == 0
            ? StepOutcome.Ok(Msg.Of("startup.channels.ok", _channels.Count,
                string.Join(", ", _channels.Select(c => c.Id + " " + c.Description))))
            : StepOutcome.Failed(Msg.Of("startup.channels.failed", failures.Count, failures[0]));
    }

    /// <summary>Tắt máy: ngừng nhận lệnh TRƯỚC khi đóng thiết bị (Chương 17, mục 17.7).</summary>
    public void Stop()
    {
        foreach (IChannel channel in _channels)
        {
            channel.Stop();
        }
    }

    public void Dispose()
    {
        foreach (IChannel channel in _channels)
        {
            channel.Dispose();
        }
        _channels.Clear();
    }
}
