using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace MeoVision.UI;

/// <summary>
/// Biểu tượng thanh nút màn chính, vẽ theo các ký hiệu của mockup v2.2 (khung 32×32, màu
/// #3E6E9E). Vẽ bằng GDI+ thay vì file ảnh: không có file nào để quên chép khi phát hành, và bản
/// "khoá" (mờ + ổ khoá) sinh từ cùng một hình.
/// </summary>
public static class ToolbarIcons
{
    public const int Size = 34;
    private static readonly Color Ink = Color.FromArgb(0x3E, 0x6E, 0x9E);
    private static readonly Dictionary<string, Bitmap> Cache = new();

    /// <summary>Tên: home, camera, calib, job, comm, product, history, settings, user, exit.</summary>
    public static Bitmap Get(string name, bool locked = false)
    {
        string key = name + (locked ? ":locked" : string.Empty);
        if (!Cache.TryGetValue(key, out Bitmap? icon))
        {
            icon = locked ? Lock(Draw(name)) : Draw(name);
            Cache[key] = icon;
        }
        return icon;
    }

    private static Bitmap Draw(string name)
    {
        Bitmap b = new(Size, Size);
        using Graphics g = Graphics.FromImage(b);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.ScaleTransform(Size / 32f, Size / 32f);
        using SolidBrush ink = new(Ink);
        using SolidBrush white = new(Color.White);
        using Pen thick = new(Ink, 3f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        switch (name)
        {
            case "home":
                g.FillPolygon(ink, new PointF[]
                {
                    new(4, 15), new(16, 5), new(28, 15), new(28, 27), new(20, 27), new(20, 19),
                    new(12, 19), new(12, 27), new(4, 27),
                });
                break;
            case "camera":
                g.FillRectangle(ink, 3, 9, 26, 18);
                g.FillRectangle(ink, 11, 5, 10, 5);
                g.FillEllipse(white, 10, 12, 12, 12);
                g.FillEllipse(ink, 12.6f, 14.6f, 6.8f, 6.8f);
                break;
            case "calib":
                g.FillRectangle(white, 4, 4, 24, 24);
                using (Pen p = new(Ink, 2f))
                {
                    g.DrawRectangle(p, 4, 4, 24, 24);
                }
                foreach ((int x, int y) in new[] { (4, 4), (20, 4), (12, 12), (4, 20), (20, 20) })
                {
                    g.FillRectangle(ink, x, y, 8, 8);
                }
                break;
            case "job":
                g.DrawEllipse(thick, 5, 5, 22, 22);
                g.FillEllipse(ink, 11.5f, 11.5f, 9, 9);
                using (Pen p = new(Ink, 2.4f))
                {
                    g.DrawLine(p, 16, 1, 16, 8);
                    g.DrawLine(p, 16, 24, 16, 31);
                    g.DrawLine(p, 1, 16, 8, 16);
                    g.DrawLine(p, 24, 16, 31, 16);
                }
                break;
            case "comm":
                g.FillRectangle(ink, 2, 6, 12, 9);
                g.FillRectangle(ink, 18, 17, 12, 9);
                using (Pen p = new(Ink, 2.2f))
                {
                    g.DrawLines(p, new PointF[] { new(8, 15), new(8, 21), new(18, 21) });
                    g.DrawLines(p, new PointF[] { new(24, 17), new(24, 11), new(14, 11) });
                }
                break;
            case "product":
                g.FillPolygon(ink, new PointF[]
                {
                    new(3, 9), new(16, 3), new(29, 9), new(29, 23), new(16, 29), new(3, 23),
                });
                using (Pen p = new(Color.White, 1.8f))
                {
                    g.DrawLines(p, new PointF[] { new(3, 9), new(16, 15), new(29, 9) });
                    g.DrawLine(p, 16, 15, 16, 29);
                }
                break;
            case "history":
                g.DrawEllipse(thick, 4, 4, 24, 24);
                g.DrawLines(thick, new PointF[] { new(16, 9), new(16, 17), new(21, 21) });
                break;
            case "settings":
                using (Pen teeth = new(Ink, 5f) { DashPattern = new[] { 0.9f, 0.9f } })
                {
                    g.DrawEllipse(teeth, 4.5f, 4.5f, 23, 23);
                }
                g.FillEllipse(ink, 7, 7, 18, 18);
                g.FillEllipse(white, 12, 12, 8, 8);
                break;
            case "user":
                g.FillEllipse(ink, 9.5f, 4.5f, 13, 13);
                g.FillPie(ink, 4, 19, 24, 20, 180, 180);
                break;
            case "exit":
                g.DrawArc(new Pen(Ink, 3.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round },
                    5, 6, 22, 22, -55, 290);
                using (Pen p = new(Ink, 3.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    g.DrawLine(p, 16, 3, 16, 15);
                }
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(name), name, null);
        }
        return b;
    }

    /// <summary>Bản khoá: hình mờ 45% + ổ khoá xám góc trên phải (như mockup).</summary>
    private static Bitmap Lock(Bitmap source)
    {
        Bitmap b = new(Size, Size);
        using Graphics g = Graphics.FromImage(b);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (ImageAttributes faded = new())
        {
            faded.SetColorMatrix(new ColorMatrix { Matrix33 = 0.45f });
            g.DrawImage(source, new Rectangle(0, 0, Size, Size), 0, 0, Size, Size,
                GraphicsUnit.Pixel, faded);
        }
        Color grey = Color.FromArgb(0x6B, 0x71, 0x77);
        using SolidBrush body = new(grey);
        using Pen shackle = new(grey, 2f);
        g.FillRectangle(body, 21, 9, 12, 9);
        g.DrawArc(shackle, 23.5f, 2.5f, 7, 9, 180, 180);
        g.DrawLine(shackle, 23.5f, 7, 23.5f, 9);
        g.DrawLine(shackle, 30.5f, 7, 30.5f, 9);
        return b;
    }
}
