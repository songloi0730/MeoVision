using System;
using System.Threading;
using System.Threading.Tasks;
using MeoVision.Core;
using MeoVision.Core.Contracts;
using Microsoft.Extensions.Logging;

namespace MeoVision.WorkStation.Demo;

// In ra console đúng 4 tín hiệu Trigger/Busy/Done/Ack (Ch15 Bảng 15.1) — narrate tín hiệu THÔ ở
// đây; PlcHandshakeSimulator chỉ lo phần narrate riêng của vision (Outcome/Score), không đụng vào
// 4 tín hiệu này — tách đúng "dây" (IPlcCommunication) khỏi "nghiệp vụ" (PlcHandshakeSimulator).
public sealed class SimulatedPlcCommunication : IPlcCommunication
{
    private readonly ILogger<SimulatedPlcCommunication> _logger;

    public SimulatedPlcCommunication(ILogger<SimulatedPlcCommunication> logger)
    {
        Guard.ThrowIfNull(logger, nameof(logger));
        _logger = logger;
    }

    public bool IsConnected { get; private set; }

    public Task ConnectAsync(CancellationToken ct = default)
    {
        IsConnected = true;
        return Task.CompletedTask;
    }

    public Task DisconnectAsync(CancellationToken ct = default)
    {
        IsConnected = false;
        return Task.CompletedTask;
    }

    public Task WaitForTriggerAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        Console.WriteLine("[PLC]    Trigger=1");
        return Task.CompletedTask;
    }

    public Task SetBusyAsync(bool busy, CancellationToken ct = default)
    {
        Console.WriteLine(busy ? "[Vision] Busy=1" : "[Vision] Busy=0");
        return Task.CompletedTask;
    }

    public Task SetDoneAsync(bool done, CancellationToken ct = default)
    {
        if (done)
        {
            Console.WriteLine("[Vision] Done=1");
        }
        else
        {
            Console.WriteLine("[Vision] Done=0");
            Console.WriteLine("[PLC]    Trigger=0, Ack=0");
        }

        return Task.CompletedTask;
    }

    public Task WaitForAckAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        Console.WriteLine("[PLC]    (đọc kết quả) Ack=1");
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        IsConnected = false;
    }
}
