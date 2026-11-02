using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace MeoVision.Engine.Simulated;

internal static class SyntheticPartRenderer
{
    private const int PixelsPerMm = 12;
    private const int MarginPx = 40;

    public static Bitmap Render(
        double widthMm,
        double heightMm,
        double offsetXPx,
        double offsetYPx,
        double angleDeg,
        int padCount,
        bool injectDefectBlob)
    {
        int canvasWidth = (int)(widthMm * PixelsPerMm) + MarginPx * 2;
        int canvasHeight = (int)(heightMm * PixelsPerMm) + MarginPx * 2;
        Bitmap bitmap = new(Math.Max(canvasWidth, 1), Math.Max(canvasHeight, 1));

        using Graphics g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.FromArgb(235, 235, 230));

        g.TranslateTransform(canvasWidth / 2f + (float)offsetXPx, canvasHeight / 2f + (float)offsetYPx);
        g.RotateTransform((float)angleDeg);

        float partWidthPx = (float)(widthMm * PixelsPerMm);
        float partHeightPx = (float)(heightMm * PixelsPerMm);
        RectangleF partRect = new(-partWidthPx / 2f, -partHeightPx / 2f, partWidthPx, partHeightPx);

        using (SolidBrush partBrush = new(Color.FromArgb(180, 190, 200)))
        using (Pen partPen = new(Color.FromArgb(60, 70, 90), 2f))
        {
            g.FillRectangle(partBrush, partRect);
            g.DrawRectangle(partPen, partRect.X, partRect.Y, partRect.Width, partRect.Height);
        }

        int safePadCount = Math.Max(padCount, 0);
        if (safePadCount > 0)
        {
            float padSpacing = partWidthPx / (safePadCount + 1);
            const float padRadius = 6f;
            using SolidBrush padBrush = new(Color.FromArgb(40, 40, 40));
            for (int i = 1; i <= safePadCount; i++)
            {
                float px = partRect.X + padSpacing * i;
                g.FillEllipse(padBrush, px - padRadius, -padRadius, padRadius * 2, padRadius * 2);
            }
        }

        if (injectDefectBlob)
        {
            using SolidBrush defectBrush = new(Color.FromArgb(200, 40, 30));
            float bx = partRect.X + partRect.Width * 0.7f;
            float by = partRect.Y + partRect.Height * 0.3f;
            g.FillEllipse(defectBrush, bx, by, 14f, 14f);
        }

        // Reset transform trước khi vẽ watermark — chữ không được xoay/lệch theo góc part
        g.ResetTransform();
        using (Font font = new("Consolas", 10f, FontStyle.Bold))
        using (SolidBrush watermarkBrush = new(Color.FromArgb(140, 200, 30, 30)))
        {
            g.DrawString("SIMULATED", font, watermarkBrush, 8, canvasHeight - 22);
        }

        return bitmap;
    }
}
