using System;
using System.Threading;
using MeoVision.Core;

namespace MeoVision.Application.Hosting;

/// <summary>
/// Chỉ cho một bản ứng dụng chạy trên máy (Chương 17, mục 17.5). Hai bản cùng mở sẽ tranh nhau
/// camera, cổng TCP và file log — lỗi nhận ra rất muộn và rất khó hiểu.
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    private readonly Mutex _mutex;
    private bool _owned;

    private SingleInstanceGuard(Mutex mutex, bool owned, bool previousInstanceCrashed)
    {
        _mutex = mutex;
        _owned = owned;
        PreviousInstanceCrashed = previousInstanceCrashed;
    }

    /// <summary>true nếu bản này giữ quyền chạy; false nếu đã có bản khác đang chạy.</summary>
    public bool IsOwner => _owned;

    /// <summary>
    /// true khi bản trước thoát mà không nhả mutex (crash, bị kill) — nên ghi log và kiểm lại phần
    /// cứng kỹ hơn bình thường, vì lần tắt trước đã không đi qua quy trình tắt máy.
    /// </summary>
    public bool PreviousInstanceCrashed { get; }

    public static SingleInstanceGuard Acquire(string applicationId)
    {
        Guard.ThrowIfNull(applicationId, nameof(applicationId));
        // "Global\": chặn cả khi có hai phiên đăng nhập Windows (Remote Desktop) trên cùng máy.
        Mutex mutex = new(false, @"Global\" + applicationId + ".SingleInstance");
        try
        {
            bool owned = mutex.WaitOne(TimeSpan.Zero);
            return new SingleInstanceGuard(mutex, owned, false);
        }
        catch (AbandonedMutexException)
        {
            // Mutex bị bỏ rơi: lần này VẪN lấy được quyền sở hữu, nhưng phải biết điều đó.
            return new SingleInstanceGuard(mutex, true, true);
        }
    }

    public void Dispose()
    {
        if (_owned)
        {
            _mutex.ReleaseMutex();
            _owned = false;
        }
        _mutex.Dispose();
    }
}
