using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System;
using MeoVision.App.Audit;
using MeoVision.App.Calibration;
using MeoVision.App.Comm;
using MeoVision.App.Commands;
using MeoVision.App.Devices.Lights;
using MeoVision.App.Devices;
using MeoVision.App.Hosting;
using MeoVision.App.Localization;
using MeoVision.App.Maintenance;
using MeoVision.App.Products;
using MeoVision.App.Results;
using MeoVision.App.Security;
using MeoVision.App;
using MeoVision.App.Alerts;
using MeoVision.Core.Calibration;
using MeoVision.Core.Commands;
using MeoVision.Core.Devices;
using MeoVision.Core.Localization;
using MeoVision.Core.Products;
using MeoVision.Core.Storage;
using MeoVision.Core.Vision;
using MeoVision.UI.History;
using MeoVision.UI.Calibration;
using MeoVision.UI.Comm;
using MeoVision.UI.Devices;
using MeoVision.UI.Maintenance;
using MeoVision.UI.Products;
using MeoVision.UI.Settings;
using MeoVision.UI.Setup;
using MeoVision.Vision.Commands;
using MeoVision.Vision.Devices;
using MeoVision.Vision.Products;
using MeoVision.Vision.Runtime;
using MeoVision.Vision.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using Serilog;

namespace MeoVision.UI;

/// <summary>Vòng đời ứng dụng (Chương 17): khởi động có thứ tự, chạy, tắt máy có thứ tự.</summary>
public static class Program
{
    private const string ApplicationId = "MeoVision";

    [STAThread]
    public static int Main(string[] args)
    {
        // Chương 24: các chế độ dòng lệnh — không mở giao diện, in ra cửa sổ lệnh, trả mã thoát.
        string mode = args.Length > 0 ? args[0].ToLowerInvariant() : string.Empty;
        string? argument = args.Length > 1 ? args[1] : null;
        if (mode.Length > 0)
        {
            AttachToConsole();
        }
        if (mode == "--write-release")
        {
            return WriteRelease();
        }
        // DESIGN_LOG [27]: mặc định của bản phát hành + những gì trạm này đổi (Data\settings.json).
        LoadedSettings loaded = StationSettings.Load(AppContext.BaseDirectory);
        Station = loaded;
        AppSettings settings = loaded.Settings;
        AppPaths paths = loaded.Paths;

        // Chữ giao diện cần có TRƯỚC mọi hộp thoại, kể cả hộp thoại "đang chạy" ngay dưới đây.
        string languageDir = Path.Combine(AppContext.BaseDirectory, "languages");
        Dictionary<string, Dictionary<string, string>> tables =
            JsonLocalizer.LoadTables(languageDir);
        JsonLocalizer uiText = new(tables, ReadUiLanguage(paths, settings), settings.LogLanguage,
            NullLogger.Instance);

        // 1) Một bản chạy duy nhất — kiểm TRƯỚC khi đụng camera, cổng TCP hay file log.
        using SingleInstanceGuard instance = SingleInstanceGuard.Acquire(ApplicationId);
        if (!instance.IsOwner)
        {
            if (mode.Length > 0)
            {
                Console.WriteLine(uiText.Text("app.alreadyRunning"));
                return 3;
            }
            MessageBox.Show(uiText.Text("app.alreadyRunning"), ApplicationId,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 1;
        }

        // 2) Thư mục dữ liệu + log: có log càng sớm càng tốt, mọi bước sau đều ghi vào đó.
        paths.EnsureCreated();
        Log.Logger = CreateFileLogger(paths, settings);
        if (mode is "--backup" or "--restore")
        {
            try
            {
                return RunStationBackup(mode, argument, paths, uiText);
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }

        try
        {
            // Đánh dấu "đang chạy" — chỉ tắt máy có thứ tự mới xoá (mục 17.5).
            RunMarker runMarker = RunMarker.Begin(paths.RunMarkerFile);
            using ServiceProvider services = ConfigureServices(settings, paths, uiText);
            ILoggerFactory loggerFactory = services.GetRequiredService<ILoggerFactory>();
            Microsoft.Extensions.Logging.ILogger logger = loggerFactory.CreateLogger("Host");
            // Log luôn ở MỘT ngôn ngữ cố định, độc lập với ngôn ngữ người vận hành đang chọn.
            JsonLocalizer logText = new(tables, settings.LogLanguage, settings.LogLanguage,
                loggerFactory.CreateLogger("Localization"));
            uiText.LanguageChanged += (_, _) => SaveUiLanguage(paths, uiText.Language);

            // 3) Lưới an toàn cuối cùng cho mọi luồng.
            CrashReporter crash = new(logger, paths.Diagnostics);
            crash.InstallProcessWideHandlers();
            System.Windows.Forms.Application.SetUnhandledExceptionMode(
                UnhandledExceptionMode.CatchException);
            System.Windows.Forms.Application.ThreadException += (_, e) =>
            {
                string? file = crash.Report(e.Exception, "UI thread", isTerminating: false);
                string message = uiText.Text("app.unexpectedError", e.Exception.Message,
                    file ?? "-");
                MessageBox.Show(message, ApplicationId, MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            };
            if (runMarker.PreviousRunEndedAbnormally)
            {
                // Lần trước không đi qua quy trình tắt máy: phần cứng có thể còn ở trạng thái
                // dở dang — log lại để người hỗ trợ đối chiếu với file sự cố (nếu có).
                logger.LogWarning("Previous run ended abnormally (started {Previous})",
                    runMarker.PreviousRun);
            }

            // 4) Khởi động có thứ tự — báo cáo quyết định có được chạy tự động hay không.
            StartupReport report = BuildStartup(services, paths, settings, tables, logger, logText)
                .RunAsync().GetAwaiter().GetResult();
            Session session = services.GetRequiredService<Session>();
            string version = InstallChecks.AppVersion(typeof(Program).Assembly);
            if (mode == "--check")
            {
                // Chương 24: kiểm cài đặt = chạy ĐÚNG chuỗi khởi động thật, rồi tắt máy có thứ tự.
                return RunCheck(services, report, version, paths, uiText, logger, logText,
                    runMarker);
            }

            System.Windows.Forms.Application.EnableVisualStyles();
            System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
            MainForm form = services.GetRequiredService<MainForm>();
            form.ShowStartupReport(report);
            if (RecoveredAdmins is { Count: > 0 } admins)
            {
                form.ShowWarning(Msg.Of("app.adminRecovered", string.Join(", ", admins)));
            }
            session.Record("app-start", report.CanRunProduction ? "ready" : "blocked",
                new Dictionary<string, string> { ["version"] = version });
            StorageJanitor janitor = services.GetRequiredService<StorageJanitor>();
            // Dọn mỗi giờ trên luồng nền — ca chạy liên tục nhiều ngày không chờ khởi động lại.
            using System.Threading.Timer cleanup = new(_ => janitor.Run(DateTime.Now), null,
                TimeSpan.FromHours(1), TimeSpan.FromHours(1));
            // Ghim DLL native của VisionPro: COM tháo cogxImagingDevice.dll khi để yên lâu làm treo cả
            // chương trình (lỗi thật 2026-11-01, xem NativeModulePin). Ngay bây giờ + mỗi 30 giây cho DLL
            // nạp sau (mở thiết bị, nạp job).
            void PinCognex()
            {
                IReadOnlyList<string> pinned = NativeModulePin.PinLoadedCognexModules();
                if (pinned.Count > 0)
                {
                    logger.LogInformation("Pinned VisionPro native modules: {Modules}",
                        string.Join(", ", pinned));
                }
            }
            PinCognex();
            using System.Threading.Timer pin = new(_ => PinCognex(), null, TimeSpan.FromSeconds(30),
                TimeSpan.FromSeconds(30));
            System.Windows.Forms.Application.Run(form);
            session.Record("app-stop", string.Empty);

            // 5) Tắt máy có thứ tự, ngược chiều khởi động.
            BuildShutdown(services, logger, logText).RunAsync().GetAwaiter().GetResult();
            runMarker.EndCleanly();
            return 0;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    private static ServiceProvider ConfigureServices(AppSettings settings, AppPaths paths,
        ILocalizer uiText)
    {
        ServiceCollection services = new();
        services.AddLogging(builder => builder.AddSerilog(dispose: true));
        services.AddSingleton(settings);
        services.AddSingleton(paths);
        services.AddSingleton(Station!);
        services.AddSingleton(uiText);
        services.AddSingleton<IVisionRuntime, CognexVisionRuntime>();
        // Chương 18: mỗi loại thiết bị một factory. Thêm hãng camera/đèn = thêm MỘT dòng ở đây.
        services.AddSingleton<IDeviceFactory, CognexGigECameraFactory>();
        services.AddSingleton<IDeviceFactory, FolderCameraFactory>();
        services.AddSingleton<IDeviceFactory, AsciiLightFactory>();
        services.AddSingleton<MachineConfigLoader>();
        services.AddSingleton(new DeviceManagerOptions());
        services.AddSingleton<DeviceManager>();
        // Chương 19: sản phẩm + bảng lệnh. Chỉ CogJobInspector biết Cognex; App dùng qua interface.
        services.AddSingleton<MachineContext>();
        services.AddSingleton<IJobInspector, CogJobInspector>();
        services.AddSingleton<ProductLoader>();
        services.AddSingleton<ProductBackup>();
        services.AddSingleton<ProductStore>();
        // Chương 20: job đã kiểm được giữ để chạy; hiệu chuẩn của máy mở theo mã.
        services.AddSingleton<ICalibrationLoader>(
            new CogCalibrationLoader(paths.MachineCalibration));
        services.AddSingleton<CommandEngine>();
        // DESIGN_LOG [28] D1, D3: bỏ qua có kiểm soát (chỉ trong bộ nhớ), dải cảnh báo.
        services.AddSingleton(new BypassService());
        services.AddSingleton(new AlertCenter());
        services.AddSingleton(sp => new AlertFeeder(sp.GetRequiredService<AlertCenter>(),
            sp.GetRequiredService<DeviceManager>(), sp.GetRequiredService<BypassService>(),
            () => sp.GetRequiredService<ChannelHost>().Channels,
            () => sp.GetRequiredService<ResultRecorder>().Counters));
        services.AddTransient(sp => new BypassForm(uiText, sp.GetRequiredService<BypassService>(),
            sp.GetRequiredService<ProductStore>(), sp.GetRequiredService<Session>()));
        services.AddSingleton<Func<BypassForm>>(sp => sp.GetRequiredService<BypassForm>);
        services.AddTransient(sp => new CommForm(uiText, sp.GetRequiredService<ChannelHost>(),
            sp.GetRequiredService<CommandService>(), sp.GetRequiredService<ProductStore>(),
            sp.GetRequiredService<Session>()));
        services.AddSingleton<Func<CommForm>>(sp => sp.GetRequiredService<CommForm>);
        services.AddTransient(sp => new CalibrationForm(uiText, sp.GetRequiredService<CalibrationService>(),
            paths, sp.GetRequiredService<Session>()));
        services.AddSingleton<Func<CalibrationForm>>(sp => sp.GetRequiredService<CalibrationForm>);
        // Chương 21: kênh nhận lệnh (TCP, PLC MC 3E) dùng chung một CommandService; hiệu chuẩn do
        // máy dẫn.
        services.AddSingleton<ICommandRunner>(sp => sp.GetRequiredService<CommandEngine>());
        services.AddSingleton<Func<MachineView>>(sp => () => MachineView.From(
            sp.GetRequiredService<MachineContext>().Config!, sp.GetRequiredService<DeviceManager>(),
            paths));
        services.AddSingleton<ICalibrationBuilder, CogNPointCalibrationBuilder>();
        services.AddSingleton<CalibrationService>();
        services.AddSingleton<CommandService>();
        services.AddSingleton<ChannelHost>();
        // Chương 22: vòng ảnh gần nhất, chạy kèm bản nháp, màn chính + Thiết lập lệnh.
        services.AddSingleton(sp => new RecentFrames(sp.GetRequiredService<CommandEngine>(),
            settings.RecentFramesPerCommand));
        services.AddSingleton<ShadowRunner>();
        services.AddTransient<SetupForm>();
        services.AddSingleton<Func<SetupForm>>(sp => sp.GetRequiredService<SetupForm>);
        // Chương 23: người dùng, nhật ký thay đổi, lưu kết quả + ảnh, dọn đĩa, lịch sử.
        services.AddSingleton(new AuditLog(paths.Audit));
        services.AddSingleton(new UserStore(paths.UsersFile, Path.Combine(paths.Root,
            UserStore.RecoveryFileName)));
        services.AddSingleton(sp => new Session(sp.GetRequiredService<UserStore>(),
            sp.GetRequiredService<AuditLog>(), TimeSpan.FromMinutes(settings.IdleLogoutMinutes)));
        services.AddSingleton<IImageStore, CogImageStore>();
        services.AddSingleton(sp => new ResultRecorder(sp.GetRequiredService<CommandEngine>(),
            paths.Results, paths.Images, sp.GetRequiredService<IImageStore>(), settings.Results,
            sp.GetRequiredService<ILogger<ResultRecorder>>()));
        services.AddSingleton(new ResultReader(paths.Results, paths.Images));
        services.AddSingleton(sp => new StorageJanitor(paths.Images, paths.Results,
            settings.Retention, StorageJanitor.FreeGbOf(paths.Images),
            sp.GetRequiredService<ILogger<StorageJanitor>>()));
        services.AddTransient<HistoryForm>();
        services.AddSingleton<Func<HistoryForm>>(sp => sp.GetRequiredService<HistoryForm>);
        // Chương 24: chạy lại ảnh đã lưu, sao lưu trạm, gói chẩn đoán.
        services.AddSingleton<ReplayRunner>();
        services.AddSingleton(new StationBackup(paths, AppContext.BaseDirectory));
        services.AddSingleton(new DiagnosticBundle(paths, AppContext.BaseDirectory));
        services.AddTransient(sp => new DiagnosticForm(uiText,
            sp.GetRequiredService<DiagnosticBundle>(), () => SystemReport(sp),
            sp.GetRequiredService<ProductStore>(), sp.GetRequiredService<Session>()));
        services.AddSingleton<Func<DiagnosticForm>>(sp => sp.GetRequiredService<DiagnosticForm>);
        // DESIGN_LOG [26]: bộ đếm giữ qua lần mở máy; form Sản phẩm và Camera & đèn (mockup v2.2).
        services.AddSingleton(new CounterStore(Path.Combine(paths.Root, "counters.json")));
        services.AddTransient(sp => new ProductForm(uiText, paths, sp.GetRequiredService<ProductStore>(),
            sp.GetRequiredService<Func<MachineView>>(), sp.GetRequiredService<Session>(),
            sp.GetRequiredService<CommandService>()));
        services.AddSingleton<Func<ProductForm>>(sp => sp.GetRequiredService<ProductForm>);
        services.AddTransient(sp => new DevicesForm(uiText, sp.GetRequiredService<DeviceManager>(),
            sp.GetRequiredService<CommandService>()));
        services.AddSingleton<Func<DevicesForm>>(sp => sp.GetRequiredService<DevicesForm>);
        services.AddTransient(sp => new StorageSettingsForm(uiText, Station!,
            sp.GetRequiredService<Session>()));
        services.AddSingleton<Func<StorageSettingsForm>>(sp => sp.GetRequiredService<StorageSettingsForm>);
        services.AddSingleton<MainForm>();
        return services.BuildServiceProvider();
    }

    private static StartupSequence BuildStartup(IServiceProvider services, AppPaths paths,
        AppSettings settings, Dictionary<string, Dictionary<string, string>> tables,
        Microsoft.Extensions.Logging.ILogger logger, ILocalizer logText)
    {
        IVisionRuntime runtime = services.GetRequiredService<IVisionRuntime>();
        DeviceManager devices = services.GetRequiredService<DeviceManager>();
        devices.FormatForLog = logText.Text;   // log thiết bị cùng ngôn ngữ cố định với log khác
        services.GetRequiredService<ProductStore>().FormatForLog = logText.Text;
        CommandEngine engine = services.GetRequiredService<CommandEngine>();
        engine.FormatForLog = logText.Text;
        engine.CreateDisplayRecords = settings.CreateDisplayRecords;
        BypassService bypass = services.GetRequiredService<BypassService>();
        engine.Bypass = bypass;
        // Hết hạn tự động: không có ai bấm nên ghi thẳng nhật ký thay đổi dưới tên hệ thống.
        AuditLog bypassAudit = services.GetRequiredService<AuditLog>();
        bypass.Expired += (_, e) => bypassAudit.Write("system", "System", "bypass-expired",
            services.GetRequiredService<ProductStore>().Current?.Id ?? string.Empty, e.Entry.Code,
            new Dictionary<string, string>
            {
                ["count"] = e.Entry.Count.ToString(CultureInfo.InvariantCulture),
                ["user"] = e.Entry.User,
                ["reason"] = e.Entry.Reason,
            });
        services.GetRequiredService<RecentFrames>();   // nghe lệnh ngay từ lệnh đầu tiên
        services.GetRequiredService<ResultRecorder>(); // ghi kết quả ngay từ lệnh đầu tiên
        ProductStore store = services.GetRequiredService<ProductStore>();
        Session session = services.GetRequiredService<Session>();
        session.ProductForAudit = () => store.Current?.Id ?? string.Empty;
        // Mọi lần đổi sản phẩm/phiên bản — kể cả do máy gửi SW — vào nhật ký thay đổi.
        store.Switched += (_, e) => session.Record("product-loaded", e.RequestedBy,
            new Dictionary<string, string>
            {
                ["previous"] = e.Previous, ["previousVersion"] = e.PreviousVersion,
                ["product"] = e.Product, ["version"] = e.Version,
            });
        return new StartupSequence(logger, logText)
            .Add("startup.step.dataFolder", isCritical: true, TimeSpan.FromSeconds(5),
                _ =>
                {
                    StepOutcome folder = DataFolderCheck.Run(paths, settings.MinFreeDiskGb);
                    // File trạm hỏng: vẫn chạy bằng mặc định, báo vàng, không ghi đè (DESIGN_LOG [27]).
                    return Task.FromResult(folder.Status == StepStatus.Failed ||
                                           Station?.Problem is null
                        ? folder
                        : StepOutcome.Warning(Station.Problem));
                })
            // Chương 24: chạy bằng quyền quản trị Windows thì mật khẩu MeoVision mất ý nghĩa.
            .Add("startup.step.account", isCritical: false, TimeSpan.FromSeconds(5),
                _ => Task.FromResult(InstallChecks.Account(WindowsAccount.UserName,
                    WindowsAccount.IsAdministrator())))
            // Không then chốt: bản dịch thiếu chỉ làm vài nhãn hiện "[khoá]", không làm sai
            // kết quả.
            .Add("startup.step.languageFiles", isCritical: false, TimeSpan.FromSeconds(5), _ =>
            {
                IReadOnlyList<string> problems = LanguageFileCheck.Compare(tables,
                    settings.LogLanguage);
                return Task.FromResult(problems.Count == 0
                    ? StepOutcome.Ok(Msg.Of("startup.languages.ok", tables.Count))
                    : StepOutcome.Failed(Msg.Of("startup.languages.incomplete", problems.Count,
                        problems.First())));
            })
            // Chương 23: tài khoản TRƯỚC VisionPro — VisionPro hỏng thì các bước sau bị bỏ qua, và
            // khi đó càng cần Kỹ sư đăng nhập được để chẩn đoán (lỗi thật 2026-10-30: danh sách
            // tài khoản trống). Chỉ cần thư mục dữ liệu.
            .Add("startup.step.users", isCritical: false, TimeSpan.FromSeconds(10),
                _ => Task.FromResult(LoadUsers(services)))
            // Đo thật: lần gọi VisionPro đầu sau khi bật máy mất ~28 s (thường 4,5–11 s) → 60 s.
            .Add("startup.step.visionRuntime", isCritical: true, TimeSpan.FromSeconds(60),
                _ => Task.FromResult(VisionRuntimeCheck.Run(runtime,
                    settings.RequiredVisionFeatures)))
            // Chương 24: file chương trình đúng bản phát hành, cùng bản VisionPro lúc build.
            .Add("startup.step.programFiles", isCritical: false, TimeSpan.FromSeconds(30),
                _ => Task.FromResult(InstallChecks.ProgramFiles(AppContext.BaseDirectory,
                    ReleaseManifest.Read(AppContext.BaseDirectory),
                    runtime.Probe(settings.RequiredVisionFeatures).Version)))
            // Chương 18: cấu hình máy sai thì không thiết bị nào đáng tin → then chốt.
            .Add("startup.step.machine", isCritical: true, TimeSpan.FromSeconds(10),
                _ => Task.FromResult(DeviceStartup.LoadMachine(
                    services.GetRequiredService<MachineConfigLoader>(), devices,
                    paths.MachineFile, services.GetRequiredService<MachineContext>())))
            // Mở thiết bị: hỏng thì cảnh báo + watchdog thử lại. Timeout của bước phải lớn hơn
            // tổng thời hạn mở của mọi thiết bị (DeviceManagerOptions.OpenTimeout mỗi cái).
            .Add("startup.step.devices", isCritical: false, TimeSpan.FromMinutes(2),
                ct => DeviceStartup.OpenDevicesAsync(devices, ct))
            // Chương 19: sản phẩm sai thì không lệnh nào đáng tin → then chốt. Timeout rộng vì
            // phải mở từng file .vpp (job lớn chục MB mất vài giây mỗi file).
            .Add("startup.step.product", isCritical: true, TimeSpan.FromMinutes(2),
                _ => Task.FromResult(ProductStartup.LoadActive(
                    services.GetRequiredService<ProductStore>(),
                    services.GetRequiredService<MachineContext>(), devices, paths)))
            // Chương 21: mở kênh SAU khi có sản phẩm — lệnh đầu tiên tới là chạy được ngay. Không
            // then chốt: cổng bị chiếm thì vẫn mở phần mềm (sửa machine.json, chạy tay), nhưng báo
            // đỏ.
            .Add("startup.step.channels", isCritical: false, TimeSpan.FromSeconds(10),
                ct => services.GetRequiredService<ChannelHost>().StartAsync(
                    services.GetRequiredService<MachineContext>().Config!, ct))
            // Chương 23: dọn ảnh/kết quả quá hạn, báo dung lượng trống của ổ ảnh.
            .Add("startup.step.storage", isCritical: false, TimeSpan.FromMinutes(2), _ =>
            {
                CleanupReport cleanup = services.GetRequiredService<StorageJanitor>()
                    .Run(DateTime.Now);
                Msg message = Msg.Of("startup.storage.ok", cleanup.DeletedFolders.Count,
                    cleanup.FreeGbAfter.ToString("0.0", CultureInfo.InvariantCulture));
                return Task.FromResult(cleanup.FreeGbAfter < settings.Retention.ImageMinFreeGb
                    ? StepOutcome.Warning(message)
                    : StepOutcome.Ok(message));
            });
    }

    private static ShutdownSequence BuildShutdown(IServiceProvider services,
        Microsoft.Extensions.Logging.ILogger logger, ILocalizer logText)
    {
        DeviceManager devices = services.GetRequiredService<DeviceManager>();
        return new ShutdownSequence(logger, logText)
            // Chương 21: ngừng nhận lệnh TRƯỚC — không lệnh mới nào chạm thiết bị sắp đóng.
            .Add("shutdown.step.stopChannels", TimeSpan.FromSeconds(5),
                services.GetRequiredService<ChannelHost>().Dispose)
            // Chương 22: thôi chạy kèm, trả các ảnh còn giữ trong RAM.
            // Chương 23: ghi nốt kết quả/ảnh đang chờ trước khi trả ảnh và đóng thiết bị.
            .Add("shutdown.step.flushResults", TimeSpan.FromSeconds(15),
                () => services.GetRequiredService<ResultRecorder>().Stop(TimeSpan.FromSeconds(10)))
            .Add("shutdown.step.releaseFrames", TimeSpan.FromSeconds(5), () =>
            {
                services.GetRequiredService<ShadowRunner>().Dispose();
                services.GetRequiredService<RecentFrames>().Dispose();
            })
            // Chương 18: đèn tắt trước (an toàn mắt, tuổi thọ LED), rồi đóng mọi thiết bị.
            .Add("shutdown.step.lightsOff", TimeSpan.FromSeconds(5),
                () => DeviceStartup.LightsOffAsync(devices))
            .Add("shutdown.step.closeDevices", TimeSpan.FromSeconds(10), devices.Dispose)
            // Chương 20: đóng job + hiệu chuẩn đang giữ (ToolBlock giữ bộ nhớ Cognex).
            .Add("shutdown.step.closeProduct", TimeSpan.FromSeconds(10),
                services.GetRequiredService<ProductStore>().Close)
            .Add("shutdown.step.closeCalibration", TimeSpan.FromSeconds(5),
                services.GetRequiredService<CalibrationService>().Dispose)
            // Luôn là bước CUỐI: mọi chu kỳ đã dừng mới được trả phần cứng.
            .Add("shutdown.step.releaseVision", TimeSpan.FromSeconds(10),
                () => services.GetRequiredService<IVisionRuntime>().ReleaseHardware());
    }

    /// <summary>Tài khoản nào vừa được khôi phục quyền Quản trị (để báo đỏ trên màn chính).</summary>
    private static IReadOnlyList<string>? RecoveredAdmins;

    /// <summary>Cấu hình hai lớp đã đọc (DESIGN_LOG [27]) — form Cài đặt ghi lại vào file trạm.</summary>
    private static LoadedSettings? Station;

    private static StepOutcome LoadUsers(IServiceProvider services)
    {
        UserStore users = services.GetRequiredService<UserStore>();
        Session session = services.GetRequiredService<Session>();
        users.Load();
        RecoveryResult recovery = users.ApplyRecovery();
        switch (recovery.Outcome)
        {
            case RecoveryOutcome.None:
                int defaults = users.Accounts.Count(a => a.IsDefaultPassword);
                Msg message = Msg.Of("startup.users.ok", users.Accounts.Count, defaults);
                return defaults > 0 ? StepOutcome.Warning(message) : StepOutcome.Ok(message);
            case RecoveryOutcome.Applied:
                // Đường tắt có chủ đích — phải ồn ào: nhật ký thay đổi, bước khởi động vàng,
                // banner đỏ.
                RecoveredAdmins = recovery.Admins;
                session.Record("admin-recovered", string.Join(", ", recovery.Admins),
                    new Dictionary<string, string> { ["owner"] = recovery.Owner });
                return StepOutcome.Warning(Msg.Of("startup.users.recovered",
                    string.Join(", ", recovery.Admins)));
            default:
                // Chương 24: file không do quản trị Windows tạo, hoặc mật khẩu quá ngắn — đã xoá,
                // không đổi gì, nhưng ai đó đã thử: ghi lại.
                session.Record("admin-recovery-rejected", recovery.Outcome.ToString(),
                    new Dictionary<string, string> { ["owner"] = recovery.Owner });
                return StepOutcome.Warning(recovery.Outcome == RecoveryOutcome.Untrusted
                    ? Msg.Of("startup.users.recoveryUntrusted", UserStore.RecoveryFileName,
                        recovery.Owner)
                    : Msg.Of("startup.users.recoveryInvalid", UserStore.RecoveryFileName));
        }
    }

    // ---------------- dòng lệnh (Chương 24) ----------------

    /// <summary>
    /// <c>--check</c>: báo cáo vào <c>diagnostics\check-*.txt</c> và ra cửa sổ lệnh, nhật ký
    /// <c>install-check</c>, tắt máy có thứ tự, mã thoát 0 / 1 / 2.
    /// </summary>
    private static int RunCheck(IServiceProvider services, StartupReport report, string version,
        AppPaths paths, ILocalizer text, Microsoft.Extensions.Logging.ILogger logger,
        ILocalizer logText, RunMarker runMarker)
    {
        string body = $"MeoVision {version} — {Environment.MachineName} — " +
                      $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}" + Environment.NewLine +
                      $"{WindowsAccount.UserName} — {paths.Root}" + Environment.NewLine +
                      Environment.NewLine + report.ToText(text);
        string file = Path.Combine(paths.Diagnostics, "check-" +
            DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".txt");
        File.WriteAllText(file, body, new UTF8Encoding(true));
        Console.WriteLine(body);
        Console.WriteLine(file);
        int exitCode = InstallChecks.ExitCode(report);
        services.GetRequiredService<Session>().Record("install-check",
            exitCode.ToString(CultureInfo.InvariantCulture),
            new Dictionary<string, string> { ["version"] = version, ["report"] = file });
        BuildShutdown(services, logger, logText).RunAsync().GetAwaiter().GetResult();
        runMarker.EndCleanly();
        return exitCode;
    }

    /// <summary>
    /// <c>--write-release</c>: chạy trong thư mục phát hành (tools/publish.ps1) — ghi release.json
    /// với mã băm từng file và bản VisionPro của máy build.
    /// </summary>
    private static int WriteRelease()
    {
        string version = InstallChecks.AppVersion(typeof(Program).Assembly);
        string visionPro = new CognexVisionRuntime(NullLogger<CognexVisionRuntime>.Instance)
            .Probe(Array.Empty<string>()).Version;
        ReleaseInfo info = ReleaseManifest.Write(AppContext.BaseDirectory, version, visionPro);
        Console.WriteLine($"{ReleaseManifest.FileName}: {info.Version}, VisionPro " +
                          $"{info.VisionPro}, {info.Files.Count} files");
        return 0;
    }

    /// <summary>
    /// <c>--backup &lt;zip&gt;</c> / <c>--restore &lt;zip&gt;</c>, khi chương trình KHÔNG chạy
    /// (người gọi đã giữ mutex một bản chạy).
    /// </summary>
    private static int RunStationBackup(string mode, string? zip, AppPaths paths, ILocalizer text)
    {
        if (zip is null || zip.Trim().Length == 0)
        {
            Console.WriteLine(text.Text("station.usage"));
            return 2;
        }
        StationBackup backup = new(paths, AppContext.BaseDirectory);
        AuditLog audit = new(paths.Audit);
        string user = Environment.UserName;
        if (mode == "--backup")
        {
            StationBackupInfo info = backup.Create(zip, user,
                InstallChecks.AppVersion(typeof(Program).Assembly));
            audit.Write(user, "windows", "station-backup", string.Empty, zip);
            Console.WriteLine(text.Text("station.backupDone", zip, info.Files.Count));
            return 0;
        }
        StationRestoreResult result = backup.Restore(zip);
        if (!result.Ok)
        {
            foreach (Msg problem in result.Problems)
            {
                Console.WriteLine(text.Text(problem));
            }
            Console.WriteLine(text.Text("station.restoreRefused", result.Problems.Count));
            return 2;
        }
        // Ồn ào như khôi phục mật khẩu: cả tài khoản lẫn cấu hình vừa bị thay.
        audit.Write(user, "windows", "station-restored", string.Empty, zip,
            new Dictionary<string, string>
            {
                ["fromMachine"] = result.Info!.Machine,
                ["createdAt"] = result.Info.CreatedAt.ToString("s", CultureInfo.InvariantCulture),
                ["movedAside"] = result.MovedAsideTo ?? string.Empty,
            });
        Console.WriteLine(text.Text("station.restoreDone", result.Info.Machine,
            result.Info.CreatedAt, result.Info.Files.Count, result.MovedAsideTo ?? "-"));
        return 0;
    }

    /// <summary>
    /// system.txt của gói chẩn đoán: đủ để người ở xa biết "máy này đang chạy cái gì". Ngôn ngữ cố
    /// định (tiếng Anh) như log — người hỗ trợ đọc được bất kể máy đặt ngôn ngữ gì.
    /// </summary>
    private static string SystemReport(IServiceProvider services)
    {
        AppSettings settings = services.GetRequiredService<AppSettings>();
        AppPaths paths = services.GetRequiredService<AppPaths>();
        ProductStore store = services.GetRequiredService<ProductStore>();
        DeviceManager devices = services.GetRequiredService<DeviceManager>();
        ReleaseInfo? release = ReleaseManifest.Read(AppContext.BaseDirectory);
        VisionRuntimeInfo vision = services.GetRequiredService<IVisionRuntime>()
            .Probe(settings.RequiredVisionFeatures);
        RecorderCounters rec = services.GetRequiredService<ResultRecorder>().Counters;
        StringBuilder sb = new();
        sb.AppendLine($"MeoVision  {InstallChecks.AppVersion(typeof(Program).Assembly)} " +
                      $"(release.json {release?.Version ?? "-"}, built on VisionPro " +
                      $"{release?.VisionPro ?? "-"})");
        sb.AppendLine($"VisionPro  {vision.Version}; licensed " +
                      string.Join(", ", vision.LicensedFeatures));
        // Không dùng Environment.OSVersion: exe không khai manifest tương thích thì nó trả
        // "6.2.9200" (Windows 8) trên cả Windows 11 — đo thật trong gói chẩn đoán đầu tiên.
        sb.AppendLine($"Windows    {RuntimeInformation.OSDescription}; " +
                      $"{RuntimeInformation.FrameworkDescription}; " +
                      $"64-bit {Environment.Is64BitProcess}");
        sb.AppendLine($"Machine    {Environment.MachineName}; user {WindowsAccount.UserName}; " +
                      $"admin {WindowsAccount.IsAdministrator()}");
        sb.AppendLine("Started    " + System.Diagnostics.Process.GetCurrentProcess().StartTime
            .ToString("s", CultureInfo.InvariantCulture));
        sb.AppendLine($"Data root  {paths.Root}; free " +
                      StorageJanitor.FreeGbOf(paths.Root)().ToString("0.0",
                          CultureInfo.InvariantCulture) + " GB");
        sb.AppendLine($"Product    {store.Current?.Id ?? "-"} version {store.CurrentVersion}");
        sb.AppendLine($"Online     {services.GetRequiredService<CommandService>().Online}");
        sb.AppendLine($"Recorder   rows {rec.Rows}, images {rec.Images}, dropped " +
                      $"{rec.DroppedImages}, failures {rec.Failures}");
        foreach (string role in devices.Roles)
        {
            sb.AppendLine($"Device     {role}: {devices.StatusOf(role)}");
        }
        return sb.ToString();
    }

    /// <summary>
    /// Exe giao diện (WinExe) không có cửa sổ lệnh riêng: gắn vào cửa sổ đã gọi nó (cmd,
    /// PowerShell). Không gắn được (chạy từ Explorer) thì vẫn còn file báo cáo.
    /// </summary>
    private static void AttachToConsole()
    {
        if (AttachConsole(AttachParentProcess))
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
        }
    }

    private const int AttachParentProcess = -1;

    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int processId);

    private static string ReadUiLanguage(AppPaths paths, AppSettings settings)
    {
        try
        {
            return File.Exists(paths.UiLanguageFile)
                ? File.ReadAllText(paths.UiLanguageFile, Encoding.UTF8).Trim()
                : settings.UiLanguage;
        }
        catch (IOException)
        {
            return settings.UiLanguage;   // không đọc được thì dùng mặc định, không chặn khởi động
        }
    }

    private static void SaveUiLanguage(AppPaths paths, string language)
    {
        try
        {
            File.WriteAllText(paths.UiLanguageFile, language, Encoding.UTF8);
        }
        catch (IOException ex)
        {
            Log.Warning(ex, "Could not save UI language");
        }
    }

    private static Serilog.ILogger CreateFileLogger(AppPaths paths, AppSettings settings)
    {
        return new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(Path.Combine(paths.Logs, "meovision-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: settings.LogRetainedFileCount,
                fileSizeLimitBytes: settings.LogFileSizeLimitMb * 1024L * 1024L,
                rollOnFileSizeLimit: true,
                flushToDiskInterval: TimeSpan.FromSeconds(2),
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] " +
                                "{SourceContext}: {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
    }
}
