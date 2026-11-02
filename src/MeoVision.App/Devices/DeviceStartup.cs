using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MeoVision.App.Hosting;
using MeoVision.Core;
using MeoVision.Core.Devices;
using MeoVision.Core.Localization;

namespace MeoVision.App.Devices;

/// <summary>
/// Hai bước khởi động của Chương 18 (mục 18.8): nạp + kiểm <c>machine.json</c> rồi tạo thiết bị
/// (THEN CHỐT — cấu hình sai thì không có gì đáng tin), và mở thiết bị (KHÔNG then chốt — một
/// camera chưa cắm thì cảnh báo, watchdog tự thử lại; Chương 19 mới quyết định sản phẩm đang chọn
/// có chạy được không, theo đúng các vai trò nó cần).
/// </summary>
public static class DeviceStartup
{
    public static StepOutcome LoadMachine(MachineConfigLoader loader, DeviceManager devices,
        string machineFile, MachineContext? context = null)
    {
        Guard.ThrowIfNull(loader, nameof(loader));
        Guard.ThrowIfNull(devices, nameof(devices));

        MachineConfigResult result = loader.Load(machineFile);
        if (!result.IsValid)
        {
            // Báo lỗi đầu tiên ngay trên màn hình; danh sách đầy đủ có trong log.
            return StepOutcome.Failed(Msg.Of("startup.machine.invalid", result.Problems.Count,
                result.Problems[0]));
        }

        MachineConfig config = result.Config!;
        if (context is not null)
        {
            context.Config = config;   // Chương 19: bước "Sản phẩm" kiểm theo đúng cấu hình này
        }
        string directory = Path.GetDirectoryName(Path.GetFullPath(machineFile))!;
        foreach (DeviceConfig camera in config.Cameras)
        {
            devices.Add(loader.FactoryFor(DeviceKind.Camera, camera.Type)
                .Create(camera, directory));
        }
        foreach (DeviceConfig light in config.Lights)
        {
            devices.Add(loader.FactoryFor(DeviceKind.Light, light.Type)
                .Create(light, directory));
        }
        return StepOutcome.Ok(Msg.Of("startup.machine.ok", config.MachineName,
            config.Cameras.Count, config.Lights.Count, config.Channels.Count));
    }

    public static async Task<StepOutcome> OpenDevicesAsync(DeviceManager devices,
        CancellationToken ct)
    {
        Guard.ThrowIfNull(devices, nameof(devices));
        IReadOnlyList<DeviceOpenResult> results = await devices.OpenAllAsync(ct)
            .ConfigureAwait(false);
        devices.StartWatchdog();   // kể cả khi tất cả đều tốt: còn để kiểm "còn sống"

        List<DeviceOpenResult> faulted = results
            .Where(r => r.Status != DeviceStatus.Connected).ToList();
        return faulted.Count == 0
            ? StepOutcome.Ok(Msg.Of("startup.devices.ok", results.Count))
            : StepOutcome.Warning(Msg.Of("startup.devices.faulted", faulted.Count,
                results.Count, string.Join(", ", faulted.Select(f => f.Role))));
    }

    /// <summary>Bước tắt máy: tắt mọi kênh của mọi đèn đang kết nối.</summary>
    public static async Task LightsOffAsync(DeviceManager devices)
    {
        Guard.ThrowIfNull(devices, nameof(devices));
        foreach (string role in devices.LightRoles)
        {
            if (devices.StatusOf(role) == DeviceStatus.Connected)
            {
                // Không biết đèn có bao nhiêu kênh ở tầng này → hỏi chính thiết bị qua manager.
                await devices.AllLightsOffAsync(role).ConfigureAwait(false);
            }
        }
    }
}
