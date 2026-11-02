using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Cognex.VisionPro;
using Cognex.VisionPro.ToolBlock;
using MeoVision.Core;
using MeoVision.Core.Commands;
using MeoVision.Core.Localization;
using MeoVision.Core.Products;
using MeoVision.Vision.Commands;

namespace MeoVision.Vision.Products;

/// <summary>
/// Mở file job bằng VisionPro thật và báo nó có gì (Chương 19, mục 19.4): có đúng là một
/// <c>CogToolBlock</c> không, các terminal input/output (tên + kiểu), script có biên dịch được không.
/// Ba điều này hai hệ tham khảo chỉ phát hiện khi máy gửi lệnh đầu tiên — ở đây phát hiện lúc nạp.
/// </summary>
public sealed class CogJobInspector : IJobInspector
{
    public JobInspection Inspect(string file)
    {
        Guard.ThrowIfNull(file, nameof(file));
        Stopwatch watch = Stopwatch.StartNew();
        object loaded;
        // Script không biên dịch → VisionPro bật hộp thoại modal trong lúc nạp; guard tự bấm OK.
        using ScriptDialogGuard guard = new();
        try
        {
            // Nạp .vpp có thể là lần chạm VisionPro đầu tiên của tiến trình (Chương 18, 18.5.3).
            lock (VisionProLock.Gate)
            {
                loaded = CogSerializer.LoadObjectFromFile(file);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException ||
                                   ex.GetType().Namespace?.StartsWith("Cognex",
                                       StringComparison.Ordinal) == true ||
                                   ex is System.Runtime.Serialization.SerializationException)
        {
            return JobInspection.Failed(Msg.Of("job.loadFailed", Path.GetFileName(file),
                ex.Message), watch.ElapsedMilliseconds);
        }

        // File lưu từ phần mềm khác có thể là kiểu khác — một hệ thật lưu ArrayList [ToolBlock, ...].
        if (loaded is not CogToolBlock block)
        {
            (loaded as IDisposable)?.Dispose();
            return JobInspection.Failed(Msg.Of("job.notToolBlock", Path.GetFileName(file),
                loaded?.GetType().FullName ?? "null"), watch.ElapsedMilliseconds);
        }

        Msg? scriptError = CompileError(block.ScriptError) ??
                           (guard.Dismissed.Count > 0
                               ? Msg.Of("job.scriptCompile", guard.Dismissed[0])
                               : null);
        // Giữ lại chính đối tượng vừa kiểm để chạy (Chương 20): không mở file lần hai, và thứ
        // chạy đúng là thứ đã kiểm. Bộ nạp sản phẩm Dispose nếu sản phẩm không hợp lệ.
        return new JobInspection(null, Terminals(block.Inputs), Terminals(block.Outputs),
            scriptError, watch.ElapsedMilliseconds,
            new CogVisionJob(block, Path.GetFileName(file), JobContract.InputImage));
    }

    private static IReadOnlyList<TerminalInfo> Terminals(CogToolBlockTerminalCollection terminals)
    {
        List<TerminalInfo> list = new();
        for (int i = 0; i < terminals.Count; i++)
        {
            CogToolBlockTerminal terminal = terminals[i];
            list.Add(new TerminalInfo(terminal.Name, terminal.ValueType));
        }
        return list;
    }

    /// <summary>
    /// <c>ScriptError</c> KHÁC RỖNG chưa chắc là lỗi (Chương 12, mục 12.5.2): script biên dịch được
    /// vẫn ghi vài dòng log ("-Referenced Assembly: ..."). Chỉ dòng "Compile Error" mới là lỗi — và
    /// khi đó VisionPro BỎ QUA script, ToolBlock vẫn chạy Accept như không có script.
    /// </summary>
    private static Msg? CompileError(string? scriptError)
    {
        if (string.IsNullOrEmpty(scriptError))
        {
            return null;
        }
        string[] lines = scriptError!.Split(new[] { '\r', '\n' },
            StringSplitOptions.RemoveEmptyEntries);
        int at = Array.FindIndex(lines, l => l.IndexOf("Compile Error",
            StringComparison.OrdinalIgnoreCase) >= 0);
        if (at < 0)
        {
            return null;
        }
        // Dòng tiêu đề + dòng lỗi đầu tiên là đủ để biết sửa ở đâu; toàn văn nằm trong editor.
        string detail = string.Join(" ", lines.Skip(at).Take(2).Select(l => l.Trim()));
        return Msg.Of("job.scriptCompile", detail);
    }
}
