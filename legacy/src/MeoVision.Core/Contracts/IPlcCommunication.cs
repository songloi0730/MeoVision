using System;
using System.Threading;
using System.Threading.Tasks;

namespace MeoVision.Core.Contracts;

// 4 tín hiệu Trigger/Busy/Done/Ack (Ch15 Bảng 15.1) — cố tình CHỈ dùng bool, không biết gì về
// VisionResult: sách dạy giao thức này "không đổi bản chất theo phương tiện truyền" (I/O rời rạc,
// thanh ghi PLC, hay bản tin TCP) — interface trung lập với phương tiện thật, y hệt tinh thần đó.
public interface IPlcCommunication : IDisposable
{
    bool IsConnected { get; }

    Task ConnectAsync(CancellationToken ct = default);

    Task DisconnectAsync(CancellationToken ct = default);

    Task WaitForTriggerAsync(CancellationToken ct = default);

    Task SetBusyAsync(bool busy, CancellationToken ct = default);

    Task SetDoneAsync(bool done, CancellationToken ct = default);

    Task WaitForAckAsync(CancellationToken ct = default);
}
