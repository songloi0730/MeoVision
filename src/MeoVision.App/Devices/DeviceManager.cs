using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MeoVision.Core;
using MeoVision.Core.Devices;
using MeoVision.Core.Localization;
using Microsoft.Extensions.Logging;

namespace MeoVision.App.Devices;

/// <summary>Thời hạn và nhịp watchdog. Giá trị mặc định hợp với camera GigE và đèn nối tiếp.</summary>
public sealed class DeviceManagerOptions
{
    public TimeSpan OpenTimeout { get; set; } = TimeSpan.FromSeconds(15);
    public TimeSpan GrabTimeout { get; set; } = TimeSpan.FromSeconds(3);
    public TimeSpan LightTimeout { get; set; } = TimeSpan.FromSeconds(1);
    public TimeSpan WatchdogInterval { get; set; } = TimeSpan.FromSeconds(2);
    public TimeSpan HealthCheckInterval { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan RetryDelayMin { get; set; } = TimeSpan.FromSeconds(2);
    public TimeSpan RetryDelayMax { get; set; } = TimeSpan.FromMinutes(1);
}

/// <summary>Kết quả mở một thiết bị lúc khởi động.</summary>
public sealed record DeviceOpenResult(string Role, string Description, DeviceStatus Status,
    Msg Detail);

/// <summary>
/// Nơi DUY NHẤT gọi tới thiết bị (Chương 18, mục 18.5). Mọi lệnh có timeout — kể cả khi SDK
/// không nghe CancellationToken; mỗi thiết bị một "cổng" để hai lệnh không chạy chồng lên nhau;
/// hỏng hoặc quá giờ → <see cref="DeviceStatus.Faulted"/>, lệnh sau bị từ chối ngay thay vì treo;
/// watchdog thử mở lại với khoảng chờ tăng dần và kiểm "còn sống" thiết bị đang rảnh.
/// </summary>
public sealed class DeviceManager : IDisposable
{
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly DeviceManagerOptions _options;
    private readonly ILogger _logger;
    private Timer? _watchdog;
    private int _watchdogBusy;
    private bool _disposed;

    public DeviceManager(DeviceManagerOptions options, ILogger<DeviceManager> logger)
    {
        Guard.ThrowIfNull(options, nameof(options));
        Guard.ThrowIfNull(logger, nameof(logger));
        _options = options;
        _logger = logger;
    }

    /// <summary>Mỗi lần một thiết bị đổi trạng thái (không phát lại khi thử mở lại vẫn hỏng).</summary>
    public event EventHandler<DeviceStatusEventArgs>? StatusChanged;

    /// <summary>
    /// Dịch thông điệp cho LOG — gán bộ dịch ngôn ngữ cố định của log (Chương 17, mục 17.6.6).
    /// Mặc định in dạng khoá thô.
    /// </summary>
    public Func<Msg, string> FormatForLog { get; set; } = m => m.ToString();

    public IReadOnlyList<string> Roles => _entries.Keys.ToList();

    public IReadOnlyList<string> CameraRoles => RolesOf<ICamera>();

    public IReadOnlyList<string> LightRoles => RolesOf<ILightController>();

    public void Add(IDevice device)
    {
        Guard.ThrowIfNull(device, nameof(device));
        _entries.Add(device.Role, new Entry(device));
    }

    public bool Has(string role) => _entries.ContainsKey(role);

    public DeviceStatus StatusOf(string role) => Get(role).Status;

    /// <summary>Mô tả thiết bị thật đang gán cho vai trò (camera serial, cổng đèn...).</summary>
    public string DescriptionOf(string role) => Get(role).Device.Description;

    /// <summary>Vai trò là camera (không phải đèn).</summary>
    public bool IsCamera(string role) => Get(role).Device is ICamera;

    /// <summary>
    /// Số kênh và cường độ tối đa của một bộ điều khiển đèn — lấy từ cấu hình, không cần kết nối.
    /// Bộ nạp sản phẩm (Chương 19) dùng để kiểm bảng lệnh.
    /// </summary>
    public (int Channels, int MaxIntensity) LightLimits(string role)
    {
        ILightController light = As<ILightController>(role);
        return (light.ChannelCount, light.MaxIntensity);
    }

    /// <summary>Mở mọi thiết bị theo thứ tự khai báo. Thiết bị hỏng không chặn thiết bị sau.</summary>
    public async Task<IReadOnlyList<DeviceOpenResult>> OpenAllAsync(CancellationToken ct = default)
    {
        List<DeviceOpenResult> results = new();
        foreach (Entry entry in _entries.Values)
        {
            await entry.Gate.WaitAsync(ct).ConfigureAwait(false);
            Msg detail = await OpenLockedAsync(entry, ct).ConfigureAwait(false);
            results.Add(new DeviceOpenResult(entry.Device.Role, entry.Device.Description,
                entry.Status, detail));
        }
        return results;
    }

    public Task<CameraImage> GrabAsync(string role, CaptureSettings settings,
        CancellationToken ct = default)
    {
        Guard.ThrowIfNull(settings, nameof(settings));
        ICamera camera = As<ICamera>(role);
        return ExecuteAsync(role, t => camera.GrabAsync(settings, t), _options.GrabTimeout,
            _options.GrabTimeout, ct);
    }

    public Task SetLightsAsync(string role, IReadOnlyList<LightChannelSetting> channels,
        CancellationToken ct = default)
    {
        Guard.ThrowIfNull(channels, nameof(channels));
        ILightController light = As<ILightController>(role);
        return ExecuteAsync(role, async t =>
        {
            await light.SetAsync(channels, t).ConfigureAwait(false);
            return true;
        }, _options.LightTimeout, _options.LightTimeout, ct);
    }

    /// <summary>Tắt mọi kênh của một bộ điều khiển đèn (tắt máy, dừng khẩn).</summary>
    public Task AllLightsOffAsync(string role, CancellationToken ct = default)
    {
        int count = As<ILightController>(role).ChannelCount;
        return SetLightsAsync(role,
            Enumerable.Range(1, count).Select(c => new LightChannelSetting(c, 0)).ToList(), ct);
    }

    /// <summary>Bắt đầu watchdog nền. Gọi sau <see cref="OpenAllAsync"/>.</summary>
    public void StartWatchdog()
    {
        _watchdog ??= new Timer(_ => _ = RunWatchdogOnceAsync(), null,
            _options.WatchdogInterval, _options.WatchdogInterval);
    }

    /// <summary>Một vòng watchdog: mở lại thiết bị hỏng đã tới hạn, kiểm thiết bị đang rảnh.</summary>
    public async Task RunWatchdogOnceAsync()
    {
        // Timer có thể bắn tiếp khi vòng trước chưa xong (mở camera mất vài giây) — bỏ lượt.
        if (Interlocked.Exchange(ref _watchdogBusy, 1) == 1)
        {
            return;
        }
        try
        {
            foreach (Entry entry in _entries.Values.ToList())
            {
                DateTime now = DateTime.UtcNow;
                if (entry.Status == DeviceStatus.Faulted && now >= entry.NextAttemptUtc)
                {
                    await TryReopenAsync(entry).ConfigureAwait(false);
                }
                else if (entry.Status == DeviceStatus.Connected &&
                         entry.Device is IHealthCheck health &&
                         now - entry.LastHealthCheckUtc >= _options.HealthCheckInterval)
                {
                    entry.LastHealthCheckUtc = now;
                    await CheckHealthAsync(entry, health).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            Interlocked.Exchange(ref _watchdogBusy, 0);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _watchdog?.Dispose();
        foreach (Entry entry in _entries.Values)
        {
            try
            {
                entry.Device.Close();
                entry.Device.Dispose();
            }
            catch (Exception ex)
            {
                // Đang tắt máy: một thiết bị đóng lỗi không được chặn thiết bị còn lại.
                _logger.LogError(ex, "Closing device {Role} failed", entry.Device.Role);
            }
        }
    }

    private async Task<T> ExecuteAsync<T>(string role, Func<CancellationToken, Task<T>> operation,
        TimeSpan timeout, TimeSpan gateWait, CancellationToken ct)
    {
        Entry entry = Get(role);
        if (entry.Status != DeviceStatus.Connected)
        {
            throw new DeviceException(Msg.Of("device.notConnected", role));
        }
        if (!await entry.Gate.WaitAsync(gateWait, ct).ConfigureAwait(false))
        {
            throw new DeviceException(Msg.Of("device.busy", role));
        }

        // Cổng chỉ mở lại khi thao tác THẬT SỰ kết thúc, kể cả khi đã quá giờ: lệnh sau và
        // watchdog không được chạm vào thiết bị khi lời gọi SDK cũ còn đang treo bên trong.
        CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Task<T> work = Task.Run(() => operation(cts.Token));
        _ = work.ContinueWith(_ =>
        {
            cts.Dispose();
            entry.Gate.Release();
        }, TaskScheduler.Default);

        Task finished = await Task.WhenAny(work, Task.Delay(timeout, ct)).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        if (finished != work)
        {
            cts.Cancel();
            Msg timedOut = Msg.Of("device.timeout", role, timeout.TotalMilliseconds);
            MarkFaulted(entry, timedOut);
            // Thao tác có thể xong muộn: trả ảnh về bộ nhớ, ghi lỗi nếu có.
            _ = work.ContinueWith(t =>
            {
                if (t.Status == TaskStatus.RanToCompletion)
                {
                    (t.Result as IDisposable)?.Dispose();
                }
                else if (t.IsFaulted)
                {
                    _logger.LogWarning(t.Exception, "Timed-out operation on {Role} failed later",
                        role);
                }
            }, TaskScheduler.Default);
            throw new DeviceException(timedOut);
        }

        try
        {
            return await work.ConfigureAwait(false);
        }
        catch (DeviceException ex) when (ex.Transient)
        {
            // Lỗi lẻ của một lần thao tác (Chương 20, mục 20.4): thiết bị vẫn dùng được, không
            // đánh dấu hỏng — lệnh tự quyết có chụp lại hay không.
            _logger.LogWarning("Transient error on {Role}: {Error}", role, FormatForLog(ex.Info));
            throw;
        }
        catch (DeviceException ex)
        {
            MarkFaulted(entry, ex.Info);
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Msg error = Msg.Of("device.error", role, ex.GetType().Name, ex.Message);
            MarkFaulted(entry, error);
            throw new DeviceException(error, ex);
        }
    }

    /// <summary>Mở thiết bị khi ĐANG giữ cổng của nó; luôn trả cổng.</summary>
    private async Task<Msg> OpenLockedAsync(Entry entry, CancellationToken ct)
    {
        IDevice device = entry.Device;
        Task work;
        try
        {
            work = Task.Run(() => device.OpenAsync(ct), ct);
            Task finished = await Task.WhenAny(work, Task.Delay(_options.OpenTimeout, ct))
                .ConfigureAwait(false);
            if (finished != work)
            {
                // Không trả cổng tới khi lời gọi mở thật sự kết thúc (xem ExecuteAsync); đọc
                // t.Exception để lỗi muộn không thành UnobservedTaskException.
                _ = work.ContinueWith(t =>
                {
                    _ = t.Exception;
                    entry.Gate.Release();
                }, TaskScheduler.Default);
                Msg timedOut = Msg.Of("device.timeout", device.Role,
                    _options.OpenTimeout.TotalMilliseconds);
                MarkFaulted(entry, timedOut);
                return timedOut;
            }
            await work.ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            entry.Gate.Release();
            Msg error = ex is DeviceException de
                ? de.Info
                : Msg.Of("device.error", device.Role, ex.GetType().Name, ex.Message);
            // Stack trace chỉ ở lần hỏng ĐẦU; các lần thử lại một dòng — đèn rút cáp cả đêm
            // không được làm phình log hàng nghìn stack trace giống hệt nhau.
            if (entry.FailedAttempts == 0)
            {
                _logger.LogWarning(ex, "Opening device {Role} ({Device}) failed", device.Role,
                    device.Description);
            }
            else
            {
                _logger.LogWarning(
                    "Opening device {Role} failed again (attempt {Attempt}): {Error}",
                    device.Role, entry.FailedAttempts + 1, FormatForLog(error));
            }
            MarkFaulted(entry, error);
            return error;
        }

        entry.Gate.Release();
        entry.FailedAttempts = 0;
        Msg ok = Msg.Of("device.connected", device.Description);
        SetStatus(entry, DeviceStatus.Connected, ok);
        return ok;
    }

    private async Task TryReopenAsync(Entry entry)
    {
        if (!await entry.Gate.WaitAsync(TimeSpan.Zero).ConfigureAwait(false))
        {
            return;   // lời gọi cũ còn treo — để lượt sau
        }
        _logger.LogInformation("Reconnecting {Role}, attempt {Attempt}", entry.Device.Role,
            entry.FailedAttempts + 1);
        try
        {
            entry.Device.Close();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Close before reconnect failed for {Role}", entry.Device.Role);
        }
        await OpenLockedAsync(entry, CancellationToken.None).ConfigureAwait(false);
    }

    private async Task CheckHealthAsync(Entry entry, IHealthCheck health)
    {
        try
        {
            // Cổng bận = thiết bị đang làm việc thật, bỏ qua lượt kiểm này (gateWait = 0).
            await ExecuteAsync(entry.Device.Role, async t =>
            {
                await health.CheckAsync(t).ConfigureAwait(false);
                return true;
            }, _options.LightTimeout, TimeSpan.Zero, CancellationToken.None).ConfigureAwait(false);
        }
        catch (DeviceException ex) when (ex.Info.Key == "device.busy")
        {
            // bận — không phải lỗi
        }
        catch (DeviceException ex)
        {
            _logger.LogWarning(ex, "Health check failed for {Role}", entry.Device.Role);
        }
    }

    private void MarkFaulted(Entry entry, Msg reason)
    {
        entry.FailedAttempts++;
        double backoff = _options.RetryDelayMin.TotalMilliseconds *
                         Math.Pow(2, Math.Min(entry.FailedAttempts - 1, 16));
        entry.NextAttemptUtc = DateTime.UtcNow.AddMilliseconds(
            Math.Min(backoff, _options.RetryDelayMax.TotalMilliseconds));
        SetStatus(entry, DeviceStatus.Faulted, reason);
    }

    private void SetStatus(Entry entry, DeviceStatus status, Msg reason)
    {
        DeviceStatus old = entry.Status;
        entry.Status = status;
        if (old == status)
        {
            return;
        }
        _logger.Log(status == DeviceStatus.Faulted ? LogLevel.Warning : LogLevel.Information,
            "Device {Role}: {Old} -> {New} ({Reason})", entry.Device.Role, old.ToString(),
            status.ToString(), FormatForLog(reason));
        StatusChanged?.Invoke(this, new DeviceStatusEventArgs(entry.Device.Role, status, reason));
    }

    private List<string> RolesOf<T>() =>
        _entries.Values.Where(e => e.Device is T).Select(e => e.Device.Role).ToList();

    private T As<T>(string role) where T : class, IDevice =>
        Get(role).Device as T ?? throw new DeviceException(Msg.Of("device.wrongKind", role));

    private Entry Get(string role) =>
        _entries.TryGetValue(role, out Entry? entry)
            ? entry
            : throw new DeviceException(Msg.Of("device.unknownRole", role));

    private sealed class Entry
    {
        public Entry(IDevice device) => Device = device;

        public IDevice Device { get; }

        public SemaphoreSlim Gate { get; } = new(1, 1);

        // volatile: đọc từ luồng watchdog, ghi từ luồng lệnh.
        private volatile DeviceStatus _status = DeviceStatus.Closed;

        public DeviceStatus Status
        {
            get => _status;
            set => _status = value;
        }

        public int FailedAttempts { get; set; }

        public DateTime NextAttemptUtc { get; set; }

        public DateTime LastHealthCheckUtc { get; set; }
    }
}
