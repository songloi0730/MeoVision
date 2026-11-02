using System;
using System.Collections.Generic;
using System.Linq;
using MeoVision.Core;
using MeoVision.Core.Contracts;

namespace MeoVision.Engine.Simulated;

/// <summary>
/// Đối tác giả lập của <c>CognexVisionRuntime</c> — cho máy không cài VisionPro vẫn đi hết quy
/// trình khởi động (simulation parity). Danh sách tính năng giả lập lấy đúng tên kiểu "Vx..." như
/// bản thật để cấu hình <c>RequiredVisionFeatures</c> dùng chung được cho cả hai.
/// </summary>
public sealed class SimulatedVisionRuntime : IVisionRuntime
{
    private static readonly string[] SimulatedFeatures =
    {
        "VxBlob", "VxCaliper", "VxCalib", "VxPatMax", "VxPatQuick", "VxSymbol", "VxToolGroup",
    };

    public int ReleaseHardwareCalls { get; private set; }

    public VisionRuntimeInfo Probe(IReadOnlyCollection<string> requiredFeatures)
    {
        Guard.ThrowIfNull(requiredFeatures, nameof(requiredFeatures));
        List<string> missing = requiredFeatures
            .Where(f => !SimulatedFeatures.Contains(f, StringComparer.OrdinalIgnoreCase))
            .ToList();
        return new VisionRuntimeInfo("Simulated", "sim-1.0", SimulatedFeatures, missing, true);
    }

    public void ReleaseHardware() => ReleaseHardwareCalls++;
}
