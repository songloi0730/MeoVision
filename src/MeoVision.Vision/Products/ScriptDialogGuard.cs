using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace MeoVision.Vision.Products;

/// <summary>
/// Canh hộp thoại "Unable to load script" trong lúc nạp file .vpp (Chương 19, mục 19.4.2). Đo thật
/// trên VisionPro 9.0 CR2: <c>CogSerializer.LoadObjectFromFile</c> gặp ToolBlock có script KHÔNG biên
/// dịch được thì bật một MessageBox MODAL và chặn luồng gọi cho tới khi có người bấm OK — không có
/// cờ nào để tắt. Máy chạy không người trông (khởi động, PLC gửi lệnh đổi sản phẩm) sẽ treo vô hạn.
/// Lớp này quét cửa sổ của CHÍNH tiến trình mỗi 100 ms, đọc nội dung hộp thoại rồi bấm OK thay
/// người — nội dung đó trở thành lỗi có thể báo như mọi lỗi cấu hình khác.
/// </summary>
internal sealed class ScriptDialogGuard : IDisposable
{
    private const uint WmClose = 0x0010;

    private readonly uint _processId = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
    private readonly List<string> _dismissed = new();
    private readonly HashSet<IntPtr> _seen = new();
    private readonly Timer _timer;

    public ScriptDialogGuard()
    {
        _timer = new Timer(_ => Scan(), null, TimeSpan.FromMilliseconds(100),
            TimeSpan.FromMilliseconds(100));
    }

    /// <summary>Nội dung các hộp thoại đã tự đóng, theo thứ tự xuất hiện.</summary>
    public IReadOnlyList<string> Dismissed
    {
        get { lock (_dismissed) { return _dismissed.ToArray(); } }
    }

    public void Dispose()
    {
        using ManualResetEvent done = new(false);
        _timer.Dispose(done);
        done.WaitOne(TimeSpan.FromSeconds(1));   // chờ lần quét đang chạy (nếu có) xong
    }

    private void Scan()
    {
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out uint pid);
            if (pid == _processId && IsWindowVisible(window) && ClassOf(window) == "#32770" &&
                TextOf(window).IndexOf("script", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Dismiss(window);
            }
            return true;
        }, IntPtr.Zero);
    }

    private void Dismiss(IntPtr dialog)
    {
        lock (_dismissed)
        {
            if (!_seen.Add(dialog))
            {
                return;   // đã bấm OK, hộp thoại chưa kịp đóng
            }
            StringBuilder text = new();
            EnumChildWindows(dialog, (child, _) =>
            {
                if (ClassOf(child) == "Static" && TextOf(child).Length > 0)
                {
                    text.Append(TextOf(child).Replace("\r", " ").Replace("\n", " ")).Append(' ');
                }
                return true;
            }, IntPtr.Zero);
            _dismissed.Add(text.ToString().Trim());
        }
        // Hộp thoại chỉ có nút OK coi WM_CLOSE là OK (thử thật: nạp chạy tiếp ngay sau đó).
        PostMessage(dialog, WmClose, IntPtr.Zero, IntPtr.Zero);
    }

    private static string ClassOf(IntPtr window)
    {
        StringBuilder name = new(64);
        GetClassName(window, name, name.Capacity);
        return name.ToString();
    }

    private static string TextOf(IntPtr window)
    {
        StringBuilder text = new(1024);
        GetWindowText(window, text, text.Capacity);
        return text.ToString();
    }

    private delegate bool EnumProc(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr parent, EnumProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, StringBuilder name, int capacity);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr window, StringBuilder text, int capacity);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr w, IntPtr l);
}
