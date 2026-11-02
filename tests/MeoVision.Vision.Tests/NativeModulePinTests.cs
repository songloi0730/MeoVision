using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using Cognex.VisionPro;
using FluentAssertions;
using MeoVision.Vision.Runtime;
using Xunit;

namespace MeoVision.Vision.Tests;

/// <summary>
/// Ghim DLL native của VisionPro (lỗi treo 2026-11-01): sau khi ghim, CoFreeUnusedLibrariesEx không tháo được
/// <c>cogx*.dll</c> nào nữa.
/// </summary>
public class NativeModulePinTests
{
    [DllImport("ole32.dll")]
    private static extern void CoFreeUnusedLibrariesEx(uint delayMs, uint reserved);

    private static List<string> LoadedCognex()
    {
        using Process self = Process.GetCurrentProcess();
        return self.Modules.Cast<ProcessModule>()
            .Select(m => m.ModuleName)
            .Where(n => n.StartsWith("cogx", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    [Fact]
    public void Pinned_Cognex_Modules_Survive_CoFreeUnusedLibraries()
    {
        // Nạp tầng chụp ảnh để có cogx*.dll trong tiến trình.
        CogFrameGrabbers grabbers = new();
        _ = grabbers.Count;
        LoadedCognex().Should().NotBeEmpty();

        NativeModulePin.PinLoadedCognexModules();
        List<string> before = LoadedCognex();
        for (int i = 0; i < 3; i++)
        {
            CoFreeUnusedLibrariesEx(0, 0);
        }

        LoadedCognex().Should().BeEquivalentTo(before);
        NativeModulePin.PinLoadedCognexModules().Should().BeEmpty("đã ghim hết ở lần gọi trước");
    }
}
