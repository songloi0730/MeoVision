using System;
using System.Drawing;
using Cognex.VisionPro;
using Cognex.VisionPro.Display;
using MeoVision.Core.Commands;
using MeoVision.Vision.Devices;

namespace MeoVision.UI.Display;

/// <summary>
/// Đưa một <see cref="DisplayFrame"/> lên <c>CogRecordsDisplay</c> (Chương 22, mục 22.3): record
/// của job nếu có, không thì ảnh trần. Chọn record ảnh theo khoá khai trong product.json
/// (<c>display.result</c>), thiếu thì tự chọn ảnh đầu tiên có đồ hoạ kết quả.
/// </summary>
public static class RecordView
{
    /// <summary>
    /// Nền vùng ảnh: xám trung tính sáng. Mặc định của VisionPro là xanh rất đậm — ảnh xám trên nền
    /// tối khó nhìn, và mắt phải đổi độ sáng mỗi lần nhìn từ màn hình sang chi tiết thật.
    /// </summary>
    public static readonly Color Background = Color.FromArgb(200, 200, 200);

    /// <summary>
    /// Đặt nền cho một <see cref="CogDisplay"/>. Đây là control ActiveX: đặt thuộc tính trước khi nó
    /// được tạo thì lỗi, nên chờ tới lúc có handle.
    /// </summary>
    public static void UseLightBackground(CogDisplay display)
    {
        if (display.IsHandleCreated)
        {
            display.BackColor = Background;
        }
        else
        {
            display.HandleCreated += (_, _) => display.BackColor = Background;
        }
    }

    public static void Show(CogRecordsDisplay view, DisplayFrame? frame, string preferredKey)
    {
        if (frame is null)
        {
            Clear(view);
            return;
        }
        if (frame.Record is ICogRecord record)
        {
            view.Subject = record;
            string? key = Find(record, preferredKey);
            if (key is not null)
            {
                view.SelectedRecordKey = key;
            }
        }
        else
        {
            view.Subject = null;
            view.Display.Image = CogCameraImage.From(frame.Image);
        }
        // Đo thật (spike session 23): CogRecordsDisplay đặt lại nền xanh đậm MỖI lần đổi record —
        // đặt một lần lúc tạo control là không đủ.
        view.Display.BackColor = Background;
        view.Display.Fit(true);
    }

    /// <summary>Gỡ record khỏi khung — gọi TRƯỚC khi trả ảnh của nó (ảnh đã trả không vẽ được).
    /// </summary>
    public static void Clear(CogRecordsDisplay view)
    {
        view.Subject = null;
        view.Display.Image = null;
        view.Display.BackColor = Background;
    }

    private static string? Find(ICogRecord record, string preferredKey)
    {
        string? withGraphics = null;
        string? anyImage = null;
        for (int i = 0; i < record.SubRecords.Count; i++)
        {
            ICogRecord sub = record.SubRecords[i];
            if (sub.Content is not ICogImage image || image.Width == 0)
            {
                continue;
            }
            string key = record.RecordKey + "." + sub.RecordKey;
            if (preferredKey.Length > 0 &&
                string.Equals(sub.RecordKey, preferredKey, StringComparison.Ordinal))
            {
                return key;
            }
            withGraphics ??= sub.SubRecords.Count > 0 ? key : null;
            anyImage ??= key;
        }
        return withGraphics ?? anyImage;
    }
}
