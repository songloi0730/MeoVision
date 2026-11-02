using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using MeoVision.Application;
using MeoVision.Application.Hosting;
using MeoVision.Application.Recipes;
using MeoVision.Core.Contracts;
using MeoVision.Engine;
using MeoVision.Engine.Simulated;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace MeoVision.UI;

/// <summary>Vòng đời ứng dụng (Chương 17): khởi động có thứ tự, chạy, tắt máy có thứ tự.</summary>
public static class Program
{
    private const string ApplicationId = "MeoVision";

    [STAThread]
    public static int Main()
    {
        // 1) Một bản chạy duy nhất — kiểm TRƯỚC khi đụng camera, cổng TCP hay file log.
        using SingleInstanceGuard instance = SingleInstanceGuard.Acquire(ApplicationId);
        if (!instance.IsOwner)
        {
            MessageBox.Show("MeoVision đang chạy trên máy này.", "MeoVision",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 1;
        }

        IConfigurationRoot configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .Build();
        MeoVisionHostOptions options = new();
        configuration.GetSection("MeoVision").Bind(options);

        // 2) Thư mục dữ liệu + log: có log càng sớm càng tốt, mọi bước sau đều ghi vào đó.
        AppPaths paths = AppPaths.Resolve(options.DataRoot, AppContext.BaseDirectory);
        paths.EnsureCreated();
        Log.Logger = CreateFileLogger(paths, options);

        try
        {
            ServiceCollection services = new();
            services.AddLogging(builder => builder.AddSerilog(dispose: true));
            services.AddMeoVisionApplication(options);
            if (options.UseSimulatedEngine)
            {
                services.AddSingleton<IVisionEngine, SimulatedVisionEngine>();
                services.AddSingleton<IVisionRuntime, SimulatedVisionRuntime>();
            }
            else
            {
                AddCognexServices(services);
            }
            services.AddTransient<FormMain>();

            using ServiceProvider provider = services.BuildServiceProvider();
            Microsoft.Extensions.Logging.ILogger logger = provider
                .GetRequiredService<ILoggerFactory>().CreateLogger("MeoVision.Host");

            // 3) Lưới an toàn cuối cùng cho mọi luồng.
            CrashReporter crash = new(logger, paths.Diagnostics);
            crash.InstallProcessWideHandlers();
            System.Windows.Forms.Application.SetUnhandledExceptionMode(
                UnhandledExceptionMode.CatchException);
            System.Windows.Forms.Application.ThreadException += (_, e) =>
            {
                string? file = crash.Report(e.Exception, "UI thread", isTerminating: false);
                MessageBox.Show($"Lỗi không mong muốn: {e.Exception.Message}\n\nĐã ghi: {file}",
                    "MeoVision", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            if (instance.PreviousInstanceCrashed)
            {
                logger.LogWarning("Lan chay truoc ket thuc bat thuong (mutex bi bo roi)");
            }

            // 4) Khởi động có thứ tự — báo cáo quyết định có được chạy tự động hay không.
            StartupReport report = BuildStartup(provider, paths, options, logger)
                .RunAsync().GetAwaiter().GetResult();

            System.Windows.Forms.Application.EnableVisualStyles();
            System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
            FormMain form = provider.GetRequiredService<FormMain>();
            form.ApplyStartupReport(report);
            System.Windows.Forms.Application.Run(form);

            // 5) Tắt máy có thứ tự, ngược chiều khởi động.
            BuildShutdown(provider, logger).RunAsync().GetAwaiter().GetResult();
            return 0;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    private static StartupSequence BuildStartup(IServiceProvider provider, AppPaths paths,
        MeoVisionHostOptions options, Microsoft.Extensions.Logging.ILogger logger)
    {
        IVisionRuntime runtime = provider.GetRequiredService<IVisionRuntime>();
        IRecipeRepository recipes = provider.GetRequiredService<IRecipeRepository>();

        return new StartupSequence(logger)
            .Add("Thu muc du lieu", isCritical: true, TimeSpan.FromSeconds(5),
                _ => Task.FromResult(DataFolderCheck.Run(paths, options.MinFreeDiskGb)))
            .Add("Moi truong thu vien vision", isCritical: true, TimeSpan.FromSeconds(20), _ =>
            {
                VisionRuntimeInfo info = runtime.Probe(options.RequiredVisionFeatures);
                StepOutcome outcome = !info.AllRequiredFeaturesLicensed
                    ? StepOutcome.Failed("thieu license: " + string.Join(", ", info.MissingFeatures))
                    : info.IsSimulated
                        ? StepOutcome.Warning($"dang chay ban GIA LAP ({info.Version})")
                        : StepOutcome.Ok($"{info.LibraryName} {info.Version}");
                return Task.FromResult(outcome);
            })
            .Add("Recipe dang dung", isCritical: true, TimeSpan.FromSeconds(10), async ct =>
            {
                VisionRecipe recipe = await recipes.GetAsync(options.ActiveRecipeId, ct)
                    .ConfigureAwait(false);
                return StepOutcome.Ok($"{recipe.RecipeId} (job={recipe.JobName})");
            });
    }

    private static ShutdownSequence BuildShutdown(IServiceProvider provider,
        Microsoft.Extensions.Logging.ILogger logger)
    {
        return new ShutdownSequence(logger)
            .Add("Giai phong engine vision", TimeSpan.FromSeconds(5),
                () => provider.GetRequiredService<IVisionEngine>().Dispose())
            // Luôn là bước CUỐI: mọi chu kỳ đã dừng mới được trả phần cứng.
            .Add("Tra phan cung thu vien vision", TimeSpan.FromSeconds(10),
                () => provider.GetRequiredService<IVisionRuntime>().ReleaseHardware());
    }

    // NoInlining: JIT chỉ nạp DLL Cognex khi method này THỰC SỰ được gọi — máy không cài
    // VisionPro vẫn chạy bản giả lập. Nếu viết thẳng trong Main, JIT nạp Cognex ngay khi biên
    // dịch Main và ứng dụng chết trước cả dòng đầu tiên.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void AddCognexServices(IServiceCollection services)
    {
        services.AddSingleton<IVisionEngine, CognexVisionEngine>();
        services.AddSingleton<IVisionRuntime, CognexVisionRuntime>();
    }

    private static Serilog.ILogger CreateFileLogger(AppPaths paths, MeoVisionHostOptions options)
    {
        return new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(Path.Combine(paths.Logs, "meovision-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: options.LogRetainedFileCount,
                fileSizeLimitBytes: options.LogFileSizeLimitMb * 1024L * 1024L,
                rollOnFileSizeLimit: true,
                flushToDiskInterval: TimeSpan.FromSeconds(2),
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] " +
                                "{SourceContext}: {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
    }
}
