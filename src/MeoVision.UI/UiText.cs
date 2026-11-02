using System.Windows.Forms;
using MeoVision.Core;
using MeoVision.Core.Localization;

namespace MeoVision.UI;

/// <summary>
/// Tự áp chữ lên giao diện theo QUY ƯỚC TÊN (Chương 17, mục 17.6): khoá của form là tên
/// form ("MainForm"), khoá của control/menu/cột là "TênForm.TênControl". Control không có
/// khoá trong file ngôn ngữ thì giữ nguyên chữ — nên chữ động (số đếm, đường dẫn) không cần
/// khai báo gì. Gọi lại hàm này mỗi khi đổi ngôn ngữ là đủ; không phải viết code cho từng
/// nhãn.
/// </summary>
public static class UiText
{
    public static void Apply(Form form, ILocalizer text)
    {
        Guard.ThrowIfNull(form, nameof(form));
        Guard.ThrowIfNull(text, nameof(text));

        if (text.TryText(form.Name, out string title))
        {
            form.Text = title;
        }
        ApplyToChildren(form.Name, form, text);
    }

    private static void ApplyToChildren(string prefix, Control parent, ILocalizer text)
    {
        foreach (Control control in parent.Controls)
        {
            if (control.Name.Length > 0 && text.TryText(prefix + "." + control.Name, out string s))
            {
                control.Text = s;
            }

            switch (control)
            {
                case ToolStrip strip:   // MenuStrip, StatusStrip, ToolStrip
                    ApplyToItems(prefix, strip.Items, text);
                    break;
                case ListView list:
                    foreach (ColumnHeader column in list.Columns)
                    {
                        if (column.Name.Length > 0 &&
                            text.TryText(prefix + "." + column.Name, out string header))
                        {
                            column.Text = header;
                        }
                    }
                    break;
            }

            ApplyToChildren(prefix, control, text);
        }
    }

    private static void ApplyToItems(string prefix, ToolStripItemCollection items, ILocalizer text)
    {
        foreach (ToolStripItem item in items)
        {
            if (item.Name.Length > 0 && text.TryText(prefix + "." + item.Name, out string s))
            {
                item.Text = s;
            }
            if (item is ToolStripDropDownItem dropDown)
            {
                ApplyToItems(prefix, dropDown.DropDownItems, text);
            }
        }
    }
}
