using System;
using System.Drawing;
using System.Windows.Forms;
using MeoVision.Core;

namespace MeoVision.UI;

/// <summary>
/// Đặt form theo vùng làm việc THẬT của màn hình (trừ thanh tác vụ). Kích thước cố định trong code
/// (1560×900...) vừa với 1920×1080 ở 100% nhưng lớn hơn màn khi Windows đặt 125% (vùng làm việc còn
/// ~1536×826): căn giữa màn hình thì mép trên văng ra ngoài — đúng lỗi "khung bị kéo lên trên quá".
/// </summary>
public static class ScreenFit
{
    /// <summary>Kích thước ưa thích, co cho vừa vùng làm việc (chừa lề), căn giữa màn của chuột.</summary>
    public static void Apply(Form form, Size preferred)
    {
        Guard.ThrowIfNull(form, nameof(form));
        Rectangle area = Screen.FromPoint(Cursor.Position).WorkingArea;
        int width = Math.Min(preferred.Width, area.Width - 2 * Margin);
        int height = Math.Min(preferred.Height, area.Height - 2 * Margin);
        form.StartPosition = FormStartPosition.Manual;
        form.Bounds = new Rectangle(area.X + (area.Width - width) / 2,
            area.Y + (area.Height - height) / 2, width, height);
    }

    /// <summary>Màn chính: luôn phóng to; FullScreen = không viền, che cả thanh tác vụ.</summary>
    public static void Maximize(Form form, bool fullScreen)
    {
        Guard.ThrowIfNull(form, nameof(form));
        form.StartPosition = FormStartPosition.Manual;
        form.Bounds = Screen.FromPoint(Cursor.Position).WorkingArea;
        if (fullScreen)
        {
            form.FormBorderStyle = FormBorderStyle.None;
        }
        form.WindowState = FormWindowState.Maximized;
    }

    private const int Margin = 8;
}
