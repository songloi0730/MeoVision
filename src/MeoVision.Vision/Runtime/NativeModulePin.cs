using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;

namespace MeoVision.Vision.Runtime;

/// <summary>
/// Ghim các DLL native của VisionPro (<c>cogx*.dll</c>) để Windows không bao giờ tháo chúng ra.
/// <para>
/// Lỗi thật 2026-11-01 (chồng gọi đọc từ tiến trình treo): sau vài phút không dùng, COM gọi
/// <c>CoFreeUnusedLibrariesEx</c> trên một luồng nền và tháo <c>cogxImagingDevice.dll</c>; trong lúc tháo
/// (đang giữ loader lock) DLL đó <c>SendMessage</c> tới một cửa sổ của luồng giao diện, còn luồng giao
/// diện đang chờ CLR — cần chính loader lock đó. Hai bên chờ nhau mãi: MeoVision "Not Responding" khi
/// máy để yên lâu. Spike đo được: nạp tầng chụp ảnh trên luồng MTA rồi gọi CoFreeUnusedLibrariesEx →
/// 6/7 <c>cogx*.dll</c> bị tháo; đã ghim → cả 7 còn nguyên.
/// </para>
/// </summary>
public static class NativeModulePin
{
    private const uint PinFlag = 0x1;   // GET_MODULE_HANDLE_EX_FLAG_PIN

    private static readonly HashSet<string> Pinned = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Ghim mọi <c>cogx*.dll</c> đang nạp mà chưa ghim. Gọi sau khởi động và định kỳ (DLL được nạp dần
    /// theo thiết bị/job); COM chỉ tháo DLL sau ~10 phút không dùng nên quét mỗi vài chục giây là đủ.
    /// </summary>
    /// <returns>Tên các module vừa ghim lần này.</returns>
    public static IReadOnlyList<string> PinLoadedCognexModules()
    {
        List<string> fresh = new();
        using Process self = Process.GetCurrentProcess();
        foreach (ProcessModule module in self.Modules)
        {
            string name = module.ModuleName;
            module.Dispose();
            if (!name.StartsWith("cogx", StringComparison.OrdinalIgnoreCase) ||
                !name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            lock (Pinned)
            {
                if (Pinned.Contains(name))
                {
                    continue;
                }
                if (GetModuleHandleEx(PinFlag, name, out _))
                {
                    Pinned.Add(name);
                    fresh.Add(name);
                }
            }
        }
        return fresh.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetModuleHandleEx(uint flags, string moduleName, out IntPtr module);
}
