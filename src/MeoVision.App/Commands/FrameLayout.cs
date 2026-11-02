using System;
using System.Collections.Generic;
using System.Linq;
using MeoVision.Core;
using MeoVision.Core.Products;

namespace MeoVision.App.Commands;

/// <summary>
/// Xếp các lệnh vào khung trên màn chính. Lệnh <c>display.slot = 0</c> có khung riêng; các lệnh cùng
/// số khung dùng chung một khung (hai chương trình tham khảo đều cho cấu hình như vậy). Lưới không để
/// ô trống: tối đa ba khung thì một hàng; khung cuối kéo hết hàng còn thiếu.
/// </summary>
public static class FrameLayout
{
    /// <summary>Danh sách khung theo thứ tự xuất hiện của lệnh đầu tiên trong mỗi khung.</summary>
    public static List<List<CommandConfig>> Group(IReadOnlyList<CommandConfig> commands)
    {
        Guard.ThrowIfNull(commands, nameof(commands));
        List<List<CommandConfig>> frames = new();
        Dictionary<int, List<CommandConfig>> bySlot = new();
        foreach (CommandConfig c in commands)
        {
            int slot = c.Display.Slot;
            if (slot <= 0)
            {
                frames.Add(new List<CommandConfig> { c });
            }
            else if (bySlot.TryGetValue(slot, out List<CommandConfig>? shared))
            {
                shared.Add(c);
            }
            else
            {
                shared = new List<CommandConfig> { c };
                bySlot[slot] = shared;
                frames.Add(shared);
            }
        }
        return frames;
    }

    /// <param name="configuredColumns">0 = tự chọn.</param>
    public static (int Columns, int Rows) Grid(int frames, int configuredColumns)
    {
        int n = Math.Max(1, frames);
        int columns = configuredColumns > 0
            ? Math.Min(configuredColumns, n)
            : n <= 3 ? n : n == 4 ? 2 : (int)Math.Ceiling(Math.Sqrt(n));
        return (columns, (n + columns - 1) / columns);
    }
}
