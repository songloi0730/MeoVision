using System;
using System.Collections.Generic;
using MeoVision.Core.Commands;
using MeoVision.Core.Localization;

namespace MeoVision.Core.Products;

/// <summary>
/// Mở một file job và báo nó có gì (Chương 19, mục 19.4). Tầng App gọi qua interface này nên
/// không phải biết Cognex; bản cài thật nằm ở MeoVision.Vision.
/// </summary>
public interface IJobInspector
{
    JobInspection Inspect(string file);
}

/// <summary>Một terminal của ToolBlock: tên và kiểu .NET của giá trị.</summary>
public sealed record TerminalInfo(string Name, Type ValueType);

/// <summary>
/// Kết quả mở một job. <see cref="LoadError"/> khác null = không dùng được file này; còn lại là
/// danh sách terminal và lỗi script (nếu có) để bộ nạp đối chiếu với bảng lệnh. <see cref="Job"/>
/// là chính đối tượng vừa mở, để chạy (Chương 20) — người nhận phải Dispose nếu không dùng.
/// </summary>
public sealed record JobInspection(
    Msg? LoadError,
    IReadOnlyList<TerminalInfo> Inputs,
    IReadOnlyList<TerminalInfo> Outputs,
    Msg? ScriptError,
    long ElapsedMs,
    IVisionJob? Job = null)
{
    public static JobInspection Failed(Msg error, long elapsedMs) =>
        new(error, Array.Empty<TerminalInfo>(), Array.Empty<TerminalInfo>(), null, elapsedMs);
}
