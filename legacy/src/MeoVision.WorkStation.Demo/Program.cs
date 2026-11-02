using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MeoVision.Application;
using MeoVision.Application.Alarms;
using MeoVision.Core.Contracts;
using MeoVision.Engine.Simulated;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace MeoVision.WorkStation.Demo;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        // Console mặc định (cmd.exe/PowerShell) không luôn ở UTF-8 — chữ tiếng Việt in ra có thể
        // lỗi font ("?"). Set tường minh trước khi in dòng nào; bỏ qua khi không có console handle
        // thật (VD: stdout bị redirect hoàn toàn, không ảnh hưởng chức năng chính).
        try
        {
            Console.OutputEncoding = Encoding.UTF8;
        }
        catch (IOException)
        {
        }

        IConfigurationRoot configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .Build();

        MeoVisionHostOptions options = new();
        configuration.GetSection("MeoVision").Bind(options);

        string logDirectory = Path.Combine(AppContext.BaseDirectory, options.LogDirectory);
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console()
            .WriteTo.File(Path.Combine(logDirectory, "meovision-.log"), rollingInterval: RollingInterval.Day)
            .CreateLogger();

        try
        {
            TcpPlcCommunicationOptions plcOptions = new();
            configuration.GetSection("PlcCommunication").Bind(plcOptions);
            string plcTransport = configuration.GetValue("PlcCommunication:Transport", "Tcp") ?? "Tcp";

            ServiceCollection services = new();
            services.AddLogging(builder => builder.AddSerilog(dispose: true));
            services.AddMeoVisionApplication(options);
            services.AddSingleton<IVisionEngine, SimulatedVisionEngine>();
            services.AddSingleton<PlcHandshakeSimulator>();

            if (string.Equals(plcTransport, "Simulated", StringComparison.OrdinalIgnoreCase))
            {
                services.AddSingleton<IPlcCommunication, SimulatedPlcCommunication>();
            }
            else
            {
                services.AddSingleton(plcOptions);
                services.AddSingleton<IPlcCommunication, TcpPlcCommunication>();
            }

            using ServiceProvider provider = services.BuildServiceProvider();

            InspectionCycleService cycleService = provider.GetRequiredService<InspectionCycleService>();
            IAlarmService alarmService = provider.GetRequiredService<IAlarmService>();
            alarmService.AlarmRaised += (sender, e) =>
                Console.WriteLine($"[ALARM {e.Code}] [{e.Severity}] {e.Message}");

            int demoCycleCount = configuration.GetValue("DemoCycleCount", 8);

            Console.WriteLine("MeoVision.WorkStation.Demo — mô phỏng bắt tay PLC ↔ Vision");
            Console.WriteLine($"Recipe: {options.ActiveRecipeId}");
            if (string.Equals(plcTransport, "Simulated", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("Transport PLC: Simulated (in tín hiệu ra console, không cần chương trình khác)");
            }
            else
            {
                Console.WriteLine($"Transport PLC: TCP tại {plcOptions.Host}:{plcOptions.Port} — " +
                    "chạy MeoVision.PlcTestReceiver.exe TRƯỚC rồi mới chạy chương trình này.");
            }

            Console.WriteLine();

            using CancellationTokenSource loadCts = new(TimeSpan.FromSeconds(30));
            await cycleService.LoadActiveRecipeAsync(options.ActiveRecipeId, loadCts.Token).ConfigureAwait(false);

            PlcHandshakeSimulator simulator = provider.GetRequiredService<PlcHandshakeSimulator>();
            await simulator.RunAsync(demoCycleCount).ConfigureAwait(false);

            return 0;
        }
#pragma warning disable CA1031
        catch (Exception ex)
#pragma warning restore CA1031
        {
            Log.Logger.Error(ex, "MeoVision.WorkStation.Demo dừng do lỗi chưa xử lý");
            Console.WriteLine($"LỖI: {ex.Message}");
            return 1;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}
