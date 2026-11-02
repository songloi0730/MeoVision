using System.Linq;
using MeoVision.App.Devices;
using MeoVision.App.Hosting;
using MeoVision.Core;
using MeoVision.Core.Devices;
using MeoVision.Core.Localization;

namespace MeoVision.App.Products;

/// <summary>
/// Bước khởi động "Sản phẩm" (Chương 19, mục 19.7): nạp sản phẩm đang chọn và kiểm toàn bộ theo
/// MÁY NÀY. THEN CHỐT — sản phẩm sai thì không lệnh nào đáng tin, không được chạy tự động.
/// </summary>
public static class ProductStartup
{
    public static StepOutcome LoadActive(ProductStore store, MachineContext machine,
        DeviceManager devices, AppPaths paths)
    {
        Guard.ThrowIfNull(store, nameof(store));
        Guard.ThrowIfNull(machine, nameof(machine));
        if (machine.Config is null)
        {
            return StepOutcome.Failed(Msg.Of("startup.product.noMachine"));
        }

        ProductLoadResult? result = store.LoadActive(
            MachineView.From(machine.Config, devices, paths));
        if (result is null)
        {
            // Máy mới cài: chưa chọn sản phẩm — mở được giao diện để chọn, nhưng chưa chạy được.
            return StepOutcome.Failed(Msg.Of("startup.product.none", store.List().Count));
        }
        if (!result.IsValid)
        {
            return StepOutcome.Failed(Msg.Of("startup.product.invalid", store.ActiveId!,
                result.Problems.Count, result.Problems[0]));
        }

        int jobs = result.Product!.Commands.Select(c => c.Job).Distinct().Count();
        Msg ok = Msg.Of("startup.product.ok", result.Product.Id, result.Product.Commands.Count,
            jobs, result.JobCheckMs);
        return result.Warnings.Count == 0
            ? StepOutcome.Ok(ok)
            : StepOutcome.Warning(Msg.Of("startup.product.okWithWarnings", ok,
                result.Warnings[0]));
    }
}
