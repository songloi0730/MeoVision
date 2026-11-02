using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using MeoVision.Core;

namespace MeoVision.App.Hosting;

/// <summary>
/// Phát hiện lần chạy trước KHÔNG đi qua quy trình tắt máy (Chương 17, mục 17.5): crash, bị
/// "End task", mất điện. Lúc khởi động ghi một file đánh dấu; chỉ bước cuối của tắt máy có
/// thứ tự mới xoá nó. Lần sau khởi động mà file còn đó = lần trước kết thúc bất thường.
/// </summary>
public sealed class RunMarker
{
    private readonly string _file;

    private RunMarker(string file, string? previousRun)
    {
        _file = file;
        PreviousRun = previousRun;
    }

    /// <summary>
    /// Nội dung đánh dấu của lần chạy trước (thời điểm, PID) nếu lần đó kết thúc bất thường;
    /// null nếu lần trước tắt đúng quy trình hoặc đây là lần chạy đầu.
    /// </summary>
    public string? PreviousRun { get; }

    public bool PreviousRunEndedAbnormally => PreviousRun is not null;

    /// <summary>Gọi một lần, sau khi đã giữ quyền chạy duy nhất và thư mục dữ liệu đã có.</summary>
    public static RunMarker Begin(string file)
    {
        Guard.ThrowIfNull(file, nameof(file));
        string? previous = null;
        if (File.Exists(file))
        {
            previous = File.ReadAllText(file, Encoding.UTF8).Trim();
            if (previous.Length == 0)
            {
                previous = "(empty marker)";
            }
        }

        using Process self = Process.GetCurrentProcess();
        File.WriteAllText(file,
            DateTime.Now.ToString("O", CultureInfo.InvariantCulture) + " pid " + self.Id,
            Encoding.UTF8);
        return new RunMarker(file, previous);
    }

    /// <summary>Bước CUỐI CÙNG của tắt máy có thứ tự.</summary>
    public void EndCleanly() => File.Delete(_file);
}
