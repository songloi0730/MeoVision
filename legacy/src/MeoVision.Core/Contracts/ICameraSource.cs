using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;

namespace MeoVision.Core.Contracts;

// Tách riêng khỏi IVisionEngine — đúng 2 bước độc lập "chụp" rồi "xử lý" trong chu trình sách dạy
// (Ch1 Hình 1.2). Không phải mọi IVisionEngine cần ICameraSource (SimulatedVisionEngine tự vẽ ảnh,
// không cần); CognexVisionEngine dùng ICameraSource khi VisionJobRequest không cấp sẵn ảnh.
public interface ICameraSource : IDisposable
{
    bool IsConnected { get; }

    Task ConnectAsync(CancellationToken ct = default);

    Task DisconnectAsync(CancellationToken ct = default);

    // Đẩy TriggerMode + tham số strobe của recipe xuống camera — gọi lại mỗi khi đổi recipe, không
    // phải mỗi lần chụp (khớp đúng ICogAcqFifo.Prepare() chỉ cần gọi sau khi đổi cấu hình — Ch6).
    Task ConfigureAsync(VisionRecipe recipe, CancellationToken ct = default);

    Task<Bitmap> GrabAsync(CancellationToken ct = default);
}
