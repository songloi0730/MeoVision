using System.Threading;
using System.Threading.Tasks;

namespace MeoVision.Core.Devices;

/// <summary>
/// Thiết bị kiểm được "còn sống" mà không làm gì có hại (hỏi trạng thái bộ điều khiển đèn, đọc
/// một thanh ghi camera...). Watchdog gọi định kỳ khi thiết bị rảnh — phát hiện mất kết nối
/// TRƯỚC lệnh sản xuất kế tiếp, không phải đúng lúc lệnh đó (Chương 18, mục 18.5). Ném exception
/// nghĩa là thiết bị hỏng.
/// </summary>
public interface IHealthCheck
{
    Task CheckAsync(CancellationToken ct);
}
