using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MeoVision.App.Calibration;
using MeoVision.App.Products;
using MeoVision.Core;
using MeoVision.Core.Commands;
using MeoVision.Core.Products;
using Microsoft.Extensions.Logging;

namespace MeoVision.App.Comm;

/// <summary>
/// Nhận một dòng từ kênh bất kỳ (TCP, PLC) và trả đúng một dòng trả lời (Chương 21, mục 21.2).
/// Mọi kiểu kênh dùng chung lớp này, nên quy tắc bản tin chỉ viết một lần và có test không cần
/// mạng.
/// </summary>
public sealed class CommandService
{
    public const string Heartbeat = "HB";
    public const string SwitchProduct = "SW";
    public const string CalibrationStart = "CS";
    public const string CalibrationPointCode = "CP";
    public const string CalibrationEnd = "CE";

    // Lỗi của bộ máy chạy lệnh (khoá Msg, Chương 20) → lý do ngắn, ổn định cho máy đọc.
    private static readonly Dictionary<string, string> Reasons = new(StringComparer.Ordinal)
    {
        ["cmd.noProduct"] = "NO_PRODUCT",
        ["cmd.unknown"] = "UNKNOWN",
        ["cmd.laneBusy"] = "BUSY",
        ["cmd.timeout"] = "TIMEOUT",
        ["cmd.lightFailed"] = "LIGHT",
        ["cmd.grabFailed"] = "CAMERA",
        ["cmd.jobError"] = "JOB",
        ["cmd.outputMissing"] = "OUTPUT",
        ["cmd.outputInvalid"] = "OUTPUT",
        ["cmd.outputType"] = "OUTPUT",
        ["cmd.unexpected"] = "INTERNAL",
    };

    private readonly ICommandRunner _executor;
    private readonly ProductStore _store;
    private readonly CalibrationService _calibration;
    private readonly Func<MachineView> _machine;
    private readonly ILogger<CommandService> _logger;

    public CommandService(ICommandRunner executor, ProductStore store,
        CalibrationService calibration, Func<MachineView> machine,
        ILogger<CommandService> logger)
    {
        Guard.ThrowIfNull(executor, nameof(executor));
        Guard.ThrowIfNull(store, nameof(store));
        Guard.ThrowIfNull(calibration, nameof(calibration));
        Guard.ThrowIfNull(machine, nameof(machine));
        Guard.ThrowIfNull(logger, nameof(logger));
        _executor = executor;
        _store = store;
        _calibration = calibration;
        _machine = machine;
        _logger = logger;
    }

    /// <summary>
    /// ONLINE = nhận lệnh của máy. OFFLINE trả <c>ERR,OFFLINE</c> NGAY để máy không phải chờ hết
    /// thời hạn; riêng nhịp tim vẫn trả lời (kênh còn sống, chỉ vision đang không nhận việc).
    /// </summary>
    public bool Online { get; set; } = true;

    /// <summary>Các bản tin gần nhất (màn Giao tiếp, DESIGN_LOG [28] D2).</summary>
    public CommLog Log { get; } = new();

    public async Task<string> HandleAsync(string channel, string line,
        CancellationToken ct = default)
    {
        Guard.ThrowIfNull(channel, nameof(channel));
        return await LoggedAsync(channel, line, test: false, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Gửi thử một dòng từ màn Giao tiếp như thể máy gửi trên <paramref name="channel"/>. Chỉ khi
    /// OFFLINE — đang ONLINE thì camera thuộc về lệnh của máy, gửi thử sẽ chen vào chu kỳ thật.
    /// </summary>
    public async Task<string> TestAsync(string channel, string line, CancellationToken ct = default)
    {
        Guard.ThrowIfNull(channel, nameof(channel));
        if (Online)
        {
            throw new InvalidOperationException("Test messages are only allowed while OFFLINE");
        }
        return await LoggedAsync(channel, line, test: true, ct).ConfigureAwait(false);
    }

    private async Task<string> LoggedAsync(string channel, string line, bool test,
        CancellationToken ct)
    {
        DateTime received = DateTime.Now;
        System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
        string full = await HandleCoreAsync(channel, line ?? string.Empty, test, ct)
            .ConfigureAwait(false);
        // DESIGN_LOG [29]: máy chỉ nhận kết quả; lý do vào log và màn Giao tiếp.
        (string wire, string reason) = MessageProtocol.WithoutReason(full);
        if (reason.Length > 0)
        {
            _logger.LogInformation("Channel {Channel}: {Line} -> {Reply} (reason {Reason})", channel,
                line, wire, reason);
        }
        Log.Add(received, channel, line ?? string.Empty, wire, reason, watch.ElapsedMilliseconds, test);
        return wire;
    }

    private async Task<string> HandleCoreAsync(string channel, string line, bool test,
        CancellationToken ct)
    {
        if (!MessageProtocol.TryParse(line, out Request? request, out string reason))
        {
            _logger.LogWarning("Channel {Channel}: bad message \"{Line}\"", channel, line);
            return MessageProtocol.Reply(string.Empty, string.Empty, MessageProtocol.Err, reason,
                Array.Empty<string>(), 0);
        }
        Request r = request!;
        if (r.Code == Heartbeat)
        {
            return Reply(r.Code, string.Empty, MessageProtocol.Ok);
        }
        if (!Online && !test)
        {
            return Reply(r.Code, r.Field(1), MessageProtocol.Err, "OFFLINE", OutputCount(r.Code));
        }
        switch (r.Code)
        {
            case SwitchProduct:
                return Switch(channel, r);
            case CalibrationStart:
            case CalibrationPointCode:
            case CalibrationEnd:
                CalibrationReply cal = await _calibration.HandleAsync(r, ct).ConfigureAwait(false);
                return MessageProtocol.Reply(r.Code, r.Field(0),
                    cal.Ok ? MessageProtocol.Ok : MessageProtocol.Err, cal.Reason, cal.Values,
                    cal.Values.Count);
            default:
                return await InspectAsync(channel, r, ct).ConfigureAwait(false);
        }
    }

    private async Task<string> InspectAsync(string channel, Request r, CancellationToken ct)
    {
        // Lệnh kiểm tra: <mã>,<sản phẩm>,<serial>. Tra NGUYÊN mã trong sản phẩm đang chạy.
        string product = r.Field(0), serial = r.Field(1);
        ProductConfig? current = _store.Current;
        if (current is null)
        {
            return Reply(r.Code, serial, MessageProtocol.Err, "NO_PRODUCT");
        }
        CommandConfig? command = current.Commands.Find(c => c.Code == r.Code);
        if (command is null)
        {
            return Reply(r.Code, serial, MessageProtocol.Err, "UNKNOWN");
        }
        int count = command.Outputs.Count;
        if (!string.Equals(command.Channel, channel, StringComparison.OrdinalIgnoreCase))
        {
            return Reply(r.Code, serial, MessageProtocol.Err, "CHANNEL", count);
        }
        // Chương 15, mục 15.1.6: máy nói nó đang làm sản phẩm nào; khác sản phẩm đang nạp thì
        // không chạy — một phép so chuỗi chặn cả một lô chạy nhầm job sau khi đổi model.
        if (!string.Equals(product, current.Id, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Channel {Channel}: {Code} for product {Asked}, running {Current}",
                channel, r.Code, product, current.Id);
            return Reply(r.Code, serial, MessageProtocol.Err, "PRODUCT", count);
        }

        CommandResult result = await _executor.ExecuteAsync(channel, r.Code, serial, ct)
            .ConfigureAwait(false);
        string status = result.Status switch
        {
            CommandStatus.Ok => MessageProtocol.Ok,
            CommandStatus.Ng => MessageProtocol.Ng,
            CommandStatus.Bypassed => MessageProtocol.Bypass,
            _ => MessageProtocol.Err,
        };
        string why = result.Status switch
        {
            CommandStatus.Ok => string.Empty,
            CommandStatus.Ng => result.Outputs.Any(o => !o.InLimits) ? "LIMIT" : "REJECT",
            CommandStatus.Bypassed => "BYPASS",
            _ => result.Error is not null && Reasons.TryGetValue(result.Error.Key, out string? key)
                ? key
                : "INTERNAL",
        };
        return MessageProtocol.Reply(r.Code, serial, status, why,
            result.Outputs.Select(o => o.Text), count);
    }

    private string Switch(string channel, Request r)
    {
        // Máy đổi sản phẩm (Chương 19, mục 19.6): kiểm xong mới đổi; lỗi thì sản phẩm cũ chạy
        // nguyên.
        string product = r.Field(0);
        if (product.Length == 0)
        {
            return Reply(r.Code, product, MessageProtocol.Err, "BAD_FORMAT");
        }
        ProductLoadResult result = _store.Switch(product, _machine(), "channel " + channel);
        return result.IsValid
            ? Reply(r.Code, product, MessageProtocol.Ok)
            : MessageProtocol.Reply(r.Code, product, MessageProtocol.Err, "INVALID",
                new[] { result.Problems.Count.ToString(CultureInfo.InvariantCulture) }, 1);
    }

    private int OutputCount(string code) =>
        _store.Current?.Commands.Find(c => c.Code == code)?.Outputs.Count ?? 0;

    private static string Reply(string code, string key, string status, string reason = "",
        int valueCount = 0) =>
        MessageProtocol.Reply(code, key, status, reason, Array.Empty<string>(), valueCount);
}
