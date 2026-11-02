using System;
using System.Threading;
using MeoVision.Core;

namespace MeoVision.App.Hosting;

/// <summary>
/// Chỉ cho một bản ứng dụng chạy trên máy (Chương 17, mục 17.5). Hai bản cùng mở sẽ tranh
/// nhau camera, cổng TCP và file log — lỗi nhận ra rất muộn và rất khó hiểu.
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    private readonly Mutex _mutex;
    private bool _owned;

    private SingleInstanceGuard(Mutex mutex, bool owned)
    {
        _mutex = mutex;
        _owned = owned;
    }

    /// <summary>true nếu bản này giữ quyền chạy; false nếu đã có bản khác đang chạy.</summary>
    public bool IsOwner => _owned;

    public static SingleInstanceGuard Acquire(string applicationId)
    {
        Guard.ThrowIfNull(applicationId, nameof(applicationId));
        // "Global\": chặn cả khi có hai phiên đăng nhập Windows (Remote Desktop) trên cùng
        // máy.
        Mutex mutex = new(false, @"Global\" + applicationId + ".SingleInstance");
        try
        {
            bool owned = mutex.WaitOne(TimeSpan.Zero);
            return new SingleInstanceGuard(mutex, owned);
        }
        catch (AbandonedMutexException)
        {
            // Bản trước chết khi đang giữ mutex VÀ có tiến trình khác còn mở handle tới nó.
            // Lần này vẫn lấy được quyền. Đừng dùng ngoại lệ này để phát hiện crash: nếu
            // không còn ai giữ handle, Windows huỷ hẳn mutex và ngoại lệ không bao giờ xảy
            // ra — việc đó là của RunMarker.
            return new SingleInstanceGuard(mutex, true);
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
