using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using MeoVision.Core;
using Microsoft.Extensions.Logging;

namespace MeoVision.App.Hosting;

/// <summary>
/// Lưới an toàn cuối cùng (Chương 17, mục 17.5): bắt exception không ai xử lý ở mọi luồng,
/// ghi log mức Critical và một file sự cố riêng. Đây KHÔNG phải nơi xử lý lỗi — lỗi đã tới
/// được đây tức là một chỗ nào đó thiếu try/catch đúng nghĩa; file sự cố giúp tìm ra chỗ
/// đó. Phần riêng của WinForms (Application.ThreadException) cài ở tầng UI. File sự cố và
/// log viết bằng ngôn ngữ cố định (tiếng Anh) để kỹ sư hỗ trợ đọc được ở bất kỳ nhà máy
/// nào.
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
            // Từ .NET 4.5 lỗi của task bị bỏ quên mặc định bị NUỐT IM LẶNG (chỉ làm sập
            // tiến trình nếu app.config bật ThrowUnobservedTaskExceptions) — handler này là
            // chỗ duy nhất còn thấy được nó. SetObserved: không sập kể cả khi bật cờ đó.
            e.SetObserved();
        };
    }

    /// <summary>
    /// Ghi lại một sự cố. Trả về đường dẫn file sự cố (hoặc null nếu không ghi được).
    /// </summary>
    public string? Report(Exception? exception, string source, bool isTerminating)
    {
        _logger.LogCritical(exception,
            "Unhandled exception from {Source}, terminating={Terminating}", source, isTerminating);
        try
        {
            Directory.CreateDirectory(_crashDirectory);
            string file = Path.Combine(_crashDirectory, "crash_" +
                DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture) +
                ".txt");
            File.WriteAllText(file,
                $"Time      : {DateTime.Now:O}{Environment.NewLine}" +
                $"Source    : {source}{Environment.NewLine}" +
                $"Terminate : {isTerminating}{Environment.NewLine}" +
                $"Machine   : {Environment.MachineName}{Environment.NewLine}" +
                $"Version   : {typeof(CrashReporter).Assembly.GetName().Version}" +
                $"{Environment.NewLine}{Environment.NewLine}{exception}");
            return file;
        }
        catch (Exception ex)
        {
            // Đang trong lúc sập: không được ném tiếp, chỉ ghi được gì thì ghi.
            _logger.LogError(ex, "Could not write crash file");
            return null;
        }
    }
}
