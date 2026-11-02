using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using Cognex.VisionPro.ToolBlock;
using MeoVision.Core;
using MeoVision.Core.Commands;
using MeoVision.Core.Localization;
using MeoVision.Core.Products;
using MeoVision.Vision.Devices;

namespace MeoVision.UI.Setup;

/// <summary>
/// Editor ToolBlock gốc của VisionPro trên job NHÁP (Chương 22, mục 22.7; spike v2.1). Ẩn các nút
/// mở/lưu file của editor: lưu chỉ qua "Áp dụng" của MeoVision (sao lưu + kiểm lại). Ảnh đang chọn
/// và tham số nhanh của bản nháp được đặt vào terminal input để bấm Run trong editor thấy đúng
/// kết quả; đóng editor thì gỡ ảnh ra — file .vpp lưu sau này không mang theo ảnh.
/// </summary>
public sealed class JobEditorForm : Form
{
    private static readonly string[] HiddenButtons =
        { "protect", "open", "save", "load", "import", "export", "reset" };

    private readonly CogToolBlockEditV2 _editor = new();

    private JobEditorForm(ILocalizer text, CogToolBlock block, string title)
    {
        Name = "JobEditorForm";
        Text = title;
        ScreenFit.Apply(this, new Size(1400, 860));
        Label note = new()
        {
            Name = "note", Dock = DockStyle.Bottom, Height = 28,
            TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(8, 0, 0, 0),
        };
        Button done = new()
        {
            Name = "done", Dock = DockStyle.Right, Width = 140, DialogResult = DialogResult.OK,
        };
        note.Controls.Add(done);
        // Không để editor thừa hưởng font của form — dòng trạng thái của nó chồng chữ (spike v2.1).
        _editor.Font = Control.DefaultFont;
        _editor.Dock = DockStyle.Fill;
        Controls.Add(_editor);
        Controls.Add(note);
        AcceptButton = done;
        UiText.Apply(this, text);
        Text = title;
        Shown += (_, _) =>
        {
            _editor.Subject = block;
            foreach (ToolBar bar in FindAll<ToolBar>(_editor))
            {
                foreach (ToolBarButton button in bar.Buttons)
                {
                    string tip = (button.ToolTipText ?? string.Empty).ToLowerInvariant();
                    button.Visible = !Array.Exists(HiddenButtons, tip.Contains);
                }
            }
        };
    }

    /// <summary>
    /// Mở editor (modal) trên job nháp của lệnh. Trong lúc mở, chạy kèm bỏ lượt
    /// (<see cref="App.Products.DraftSession.EditorOpen"/> do người gọi bật/tắt).
    /// </summary>
    public static void Edit(IWin32Window owner, ILocalizer text, IVisionJob job,
        CommandConfig command, DisplayFrame? image)
    {
        Guard.ThrowIfNull(job, nameof(job));
        Guard.ThrowIfNull(command, nameof(command));
        CogToolBlock block = (CogToolBlock)job.EditSubject;
        using DisplayFrame? held = image?.Share();
        if (held is not null)
        {
            block.Inputs[JobContract.InputImage].Value = CogCameraImage.From(held.Image);
        }
        foreach (TunableConfig tune in command.Tunables)
        {
            CogToolBlockTerminal terminal = block.Inputs[tune.Terminal];
            terminal.Value = Convert.ChangeType(tune.Value, terminal.ValueType,
                CultureInfo.InvariantCulture);
        }
        try
        {
            using JobEditorForm form = new(text, block,
                text.Text("JobEditorForm.title", command.Code, command.Job));
            form.ShowDialog(owner);
        }
        finally
        {
            block.Inputs[JobContract.InputImage].Value = null;
        }
    }

    private static IEnumerable<T> FindAll<T>(Control root) where T : Control
    {
        foreach (Control c in root.Controls)
        {
            if (c is T match)
            {
                yield return match;
            }
            foreach (T inner in FindAll<T>(c))
            {
                yield return inner;
            }
        }
    }
}
