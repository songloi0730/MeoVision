using System.Drawing;
using System.Windows.Forms;
using MeoVision.Core.Localization;

namespace MeoVision.UI.Setup;

/// <summary>
/// Hỏi lý do thay đổi kèm danh sách khác biệt (Chương 22): lý do đi vào tên bản sao lưu và log —
/// tháng sau còn biết vì sao ngưỡng bị đổi. Không có lý do thì không áp dụng.
/// </summary>
public sealed class ReasonDialog : Form
{
    private readonly TextBox _reason = new() { Name = "reason", Dock = DockStyle.Top };

    private ReasonDialog(ILocalizer text, string changes)
    {
        Name = "ReasonDialog";
        Size = new Size(720, 460);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = MaximizeBox = false;
        TextBox list = new()
        {
            Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, Text = changes,
            ScrollBars = ScrollBars.Vertical, TabStop = false,
        };
        Label prompt = new() { Name = "prompt", Dock = DockStyle.Top, Height = 26 };
        FlowLayoutPanel buttons = new()
        {
            Dock = DockStyle.Bottom, Height = 48, FlowDirection = FlowDirection.RightToLeft,
        };
        Button ok = new() { Name = "ok", Width = 150, Height = 38, DialogResult = DialogResult.OK };
        Button cancel = new()
        {
            Name = "cancel", Width = 110, Height = 38, DialogResult = DialogResult.Cancel,
        };
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);
        Controls.Add(list);
        Controls.Add(_reason);
        Controls.Add(prompt);
        Controls.Add(buttons);
        AcceptButton = ok;
        CancelButton = cancel;
        ok.Enabled = false;
        _reason.TextChanged += (_, _) => ok.Enabled = _reason.Text.Trim().Length > 0;
        UiText.Apply(this, text);
        // Mở ra là gõ được lý do ngay — không để con trỏ rơi vào danh sách chỉ đọc.
        Shown += (_, _) => _reason.Focus();
    }

    /// <summary>Lý do người dùng nhập; null = huỷ.</summary>
    public static string? Ask(IWin32Window owner, ILocalizer text, string changes)
    {
        using ReasonDialog dialog = new(text, changes);
        return dialog.ShowDialog(owner) == DialogResult.OK ? dialog._reason.Text.Trim() : null;
    }
}
