using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using MeoVision.Core;
using Microsoft.Extensions.Logging;

namespace MeoVision.Application.Hosting;

/// <summary>
/// Lưới an toàn cuối cùng (Chương 17, mục 17.5): bắt exception không ai xử lý ở mọi luồng, ghi
/// log mức Critical và một file sự cố riêng. Đây KHÔNG phải nơi xử lý lỗi — lỗi đã tới được đây
/// tức là một chỗ nào đó thiếu try/catch đúng nghĩa; file sự cố giúp tìm ra chỗ đó.
/// Phần riêng của WinForms (Application.ThreadException) cài ở tầng UI.
/// </summary>
public sealed class CrashReporter
{
    private readonly ILogger _logger;
    private readonly string _crashDirectory;

    public CrashReporter(ILogger logger, string crashDirectory)
    {
        Guard.ThrowIfNull(logger, nameof(logger));
        Guard.ThrowIfNull(crashDirectory, nameof(crashDirectory));
        _logger = logger;
        _crashDirectory = crashDirectory;
    }

    /// <summary>Gắn vào AppDomain (mọi luồng) và TaskScheduler (task không ai await).</summary>
    public void InstallProcessWideHandlers()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Report(e.ExceptionObject as Exception, "AppDomain", e.IsTerminating);

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Report(e.Exception, "TaskScheduler", isTerminating: false);
            e.SetObserved();   // không để .NET Framework kết thúc tiến trình vì task bị bỏ quên
        };
    }

    /// <summary>Ghi lại một sự cố. Trả về đường dẫn file sự cố (hoặc null nếu không ghi được).</summary>
    public string? Report(Exception? exception, string source, bool isTerminating)
    {
        _logger.LogCritical(exception, "Loi khong duoc xu ly tu {Source}, ket thuc={Terminating}",
            source, isTerminating);
        try
        {
            Directory.CreateDirectory(_crashDirectory);
            string file = Path.Combine(_crashDirectory, "crash_" +
                DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture) +
                ".txt");
            File.WriteAllText(file,
                $"Thoi diem : {DateTime.Now:O}{Environment.NewLine}" +
                $"Nguon     : {source}{Environment.NewLine}" +
                $"Ket thuc  : {isTerminating}{Environment.NewLine}" +
                $"May       : {Environment.MachineName}{Environment.NewLine}" +
                $"Phien ban : {typeof(CrashReporter).Assembly.GetName().Version}" +
                $"{Environment.NewLine}{Environment.NewLine}{exception}");
            return file;
        }
        catch (Exception ex)
        {
            // Đang trong lúc sập: không được ném tiếp, chỉ ghi được gì thì ghi.
            _logger.LogError(ex, "Khong ghi duoc file su co");
            return null;
        }
    }
}
