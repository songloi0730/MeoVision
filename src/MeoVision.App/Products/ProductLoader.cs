using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using MeoVision.App.Devices;
using MeoVision.App.Hosting;
using MeoVision.Core;
using MeoVision.Core.Commands;
using MeoVision.Core.Devices;
using MeoVision.Core.Localization;
using MeoVision.Core.Products;
using Newtonsoft.Json;

namespace MeoVision.App.Products;

/// <summary>
/// Những gì MÁY NÀY có để sản phẩm dùng: vai trò camera, đèn (số kênh, cường độ tối đa), kênh nhận
/// lệnh, hiệu chuẩn. Tách khỏi <see cref="DeviceManager"/> để bộ nạp test được không cần thiết bị.
/// </summary>
public sealed class MachineView
{
    public MachineView(IEnumerable<string> cameras,
        IReadOnlyDictionary<string, (int Channels, int MaxIntensity)> lights,
        IEnumerable<string> channels, Func<string, bool> calibrationExists)
    {
        Guard.ThrowIfNull(cameras, nameof(cameras));
        Guard.ThrowIfNull(lights, nameof(lights));
        Guard.ThrowIfNull(channels, nameof(channels));
        Guard.ThrowIfNull(calibrationExists, nameof(calibrationExists));
        Cameras = new HashSet<string>(cameras, StringComparer.OrdinalIgnoreCase);
        Dictionary<string, (int Channels, int MaxIntensity)> map =
            new(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, (int Channels, int MaxIntensity)> pair in lights)
        {
            map[pair.Key] = pair.Value;
        }
        Lights = map;
        Channels = new HashSet<string>(channels, StringComparer.OrdinalIgnoreCase);
        CalibrationExists = calibrationExists;
    }

    public IReadOnlyCollection<string> Cameras { get; }

    public IReadOnlyDictionary<string, (int Channels, int MaxIntensity)> Lights { get; }

    public IReadOnlyCollection<string> Channels { get; }

    public Func<string, bool> CalibrationExists { get; }

    public static MachineView From(MachineConfig machine, DeviceManager devices, AppPaths paths)
    {
        Guard.ThrowIfNull(machine, nameof(machine));
        Guard.ThrowIfNull(devices, nameof(devices));
        Guard.ThrowIfNull(paths, nameof(paths));
        return new MachineView(devices.CameraRoles,
            devices.LightRoles.ToDictionary(r => r, devices.LightLimits),
            machine.Channels.Select(c => c.Id),
            id => Directory.Exists(Path.Combine(paths.MachineCalibration, id)));
    }
}

/// <summary>
/// Kết quả nạp một sản phẩm: cấu hình, MỌI lỗi (chặn chạy) và cảnh báo (không chặn). Hợp lệ thì
/// <see cref="Loaded"/> mang các job đã mở để chạy (Chương 20); không hợp lệ thì chúng đã được đóng.
/// </summary>
public sealed record ProductLoadResult(ProductConfig? Product, IReadOnlyList<Msg> Problems,
    IReadOnlyList<Msg> Warnings, long JobCheckMs, LoadedProduct? Loaded = null)
{
    public bool IsValid => Product is not null && Problems.Count == 0;
}

/// <summary>
/// Đọc <c>product.json</c> và kiểm TOÀN BỘ trước khi sản phẩm được phép chạy (Chương 19, mục
/// 19.3–19.4): cú pháp, mã lệnh, kênh/vai trò có trên máy này không, đèn đúng khoảng, file job có
/// thật và đúng hợp đồng terminal, giới hạn hợp lệ. Gom mọi lỗi một lần — sửa file một lượt, không
/// phải khởi động lại mười lần để thấy mười lỗi.
/// </summary>
public sealed class ProductLoader
{
    /// <summary>Terminal input bắt buộc của mọi job: ảnh do bộ máy chạy lệnh đưa vào.</summary>
    public const string InputImageTerminal = JobContract.InputImage;

    /// <summary>Chờ đèn lâu hơn chừng này là cấu hình nhầm (gõ giây thay vì ms).</summary>
    public const int MaxSettleMs = 2000;

    private static readonly JsonSerializerSettings JsonSettings = new()
    {
        MissingMemberHandling = MissingMemberHandling.Error,
    };

    // Chữ hoa + số, bắt đầu bằng chữ, tối đa 8 ký tự: đủ cho "T1", "DJT12", gửi được qua PLC.
    private static readonly Regex CodePattern = new("^[A-Z][A-Z0-9]{0,7}$", RegexOptions.Compiled);

    /// <summary>Mã dành cho lệnh hệ thống (Chương 21) — sản phẩm không được dùng.</summary>
    private static readonly string[] ReservedCodes = { "SW", "HB" };

    private static readonly Type[] NumericTypes =
    {
        typeof(double), typeof(float), typeof(int), typeof(long), typeof(short), typeof(decimal),
        typeof(byte),
    };

    /// <summary>Kiểu output gửi được cho máy: số, bool (1/0), chuỗi (mã đọc được).</summary>
    private static readonly Type[] SendableTypes =
        NumericTypes.Concat(new[] { typeof(bool), typeof(string) }).ToArray();

    private readonly IJobInspector _inspector;
    private readonly ICalibrationLoader _calibrationLoader;

    public ProductLoader(IJobInspector inspector, ICalibrationLoader calibrationLoader)
    {
        Guard.ThrowIfNull(inspector, nameof(inspector));
        Guard.ThrowIfNull(calibrationLoader, nameof(calibrationLoader));
        _inspector = inspector;
        _calibrationLoader = calibrationLoader;
    }

    public ProductLoadResult Load(ProductPaths paths, string expectedId, MachineView machine)
    {
        Guard.ThrowIfNull(paths, nameof(paths));
        Guard.ThrowIfNull(expectedId, nameof(expectedId));
        Guard.ThrowIfNull(machine, nameof(machine));
        if (!File.Exists(paths.ProductFile))
        {
            return Fail(Msg.Of("product.missing", paths.ProductFile));
        }

        ProductConfig? product;
        try
        {
            product = JsonConvert.DeserializeObject<ProductConfig>(
                File.ReadAllText(paths.ProductFile, Encoding.UTF8), JsonSettings);
        }
        catch (JsonException ex)
        {
            return Fail(Msg.Of("product.invalidJson", paths.ProductFile, ex.Message));
        }
        if (product is null)
        {
            return Fail(Msg.Of("product.invalidJson", paths.ProductFile, "empty"));
        }

        List<Msg> problems = new();
        List<Msg> warnings = new();
        if (product.SchemaVersion != ProductConfig.CurrentSchemaVersion)
        {
            problems.Add(Msg.Of("product.schemaVersion", product.SchemaVersion,
                ProductConfig.CurrentSchemaVersion));
        }
        if (!string.Equals(product.Id, expectedId, StringComparison.OrdinalIgnoreCase))
        {
            // Thư mục chép từ sản phẩm khác mà quên sửa id: hai sản phẩm cùng id → kết quả và ảnh
            // ghi lẫn vào nhau.
            problems.Add(Msg.Of("product.idMismatch", product.Id, expectedId));
        }
        if (product.Name.Trim().Length == 0)
        {
            problems.Add(Msg.Of("product.nameEmpty"));
        }
        if (product.Commands.Count == 0)
        {
            problems.Add(Msg.Of("product.noCommands"));
        }

        Dictionary<string, JobInspection> jobs = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, ICalibration> calibrations = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> codes = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < product.Commands.Count; i++)
        {
            CheckCommand(product.Commands[i], i + 1, paths, machine, codes, jobs, calibrations,
                problems);
        }

        // Cảnh báo: file .vpp nằm trong thư mục nhưng không lệnh nào dùng — bản sao, bản thử cũ.
        // Không chặn chạy, nhưng hay là nguồn nhầm lẫn khi sửa nhầm file.
        if (Directory.Exists(paths.Jobs))
        {
            List<string> unused = Directory.GetFiles(paths.Jobs, "*.vpp")
                .Select(f => Path.GetFileName(f)).Where(f => !jobs.ContainsKey(f))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
            if (unused.Count > 0)
            {
                warnings.Add(Msg.Of("product.unusedJobs", unused.Count, string.Join(", ", unused)));
            }
        }
        long jobCheckMs = jobs.Values.Sum(j => j.ElapsedMs);
        if (problems.Count > 0)
        {
            // Không chạy được thì không giữ gì: job và hiệu chuẩn đã mở phải đóng ngay.
            foreach (JobInspection job in jobs.Values)
            {
                job.Job?.Dispose();
            }
            foreach (ICalibration calibration in calibrations.Values)
            {
                calibration.Dispose();
            }
            return new ProductLoadResult(product, problems, warnings, jobCheckMs);
        }
        LoadedProduct loaded = new(product,
            jobs.ToDictionary(p => p.Key, p => p.Value.Job!, StringComparer.OrdinalIgnoreCase),
            calibrations, ProductBackup.ContentVersion(paths));
        return new ProductLoadResult(product, problems, warnings, jobCheckMs, loaded);
    }

    private void CheckCommand(CommandConfig c, int index, ProductPaths paths, MachineView machine,
        HashSet<string> codes, Dictionary<string, JobInspection> jobs,
        Dictionary<string, ICalibration> calibrations, List<Msg> problems)
    {
        string label = c.Code.Trim().Length > 0 ? c.Code : "#" + index;
        if (!CodePattern.IsMatch(c.Code))
        {
            problems.Add(Msg.Of("product.cmd.badCode", label));
        }
        else if (ReservedCodes.Contains(c.Code, StringComparer.OrdinalIgnoreCase))
        {
            problems.Add(Msg.Of("product.cmd.reservedCode", c.Code));
        }
        else if (!codes.Add(c.Code))
        {
            problems.Add(Msg.Of("product.cmd.duplicate", c.Code));
        }

        if (!machine.Channels.Contains(c.Channel))
        {
            problems.Add(Msg.Of("product.cmd.noChannel", label, c.Channel,
                string.Join(", ", machine.Channels)));
        }
        if (!machine.Cameras.Contains(c.Camera))
        {
            problems.Add(Msg.Of("product.cmd.noCamera", label, c.Camera,
                string.Join(", ", machine.Cameras)));
        }
        CheckCalibration(c.Calibration, label, machine, calibrations, problems);
        CheckCapture(label, c.ExposureMs, c.Brightness, c.Contrast, problems);
        if (!CommandKind.IsValid(c.Kind))
        {
            problems.Add(Msg.Of("product.cmd.badKind", label, c.Kind));
        }
        if (c.Display.Slot < 0)
        {
            problems.Add(Msg.Of("product.cmd.badSlot", label, c.Display.Slot));
        }
        if (c.TimeoutMs <= 0)
        {
            problems.Add(Msg.Of("product.cmd.badTimeout", label, c.TimeoutMs));
        }
        if (c.Retry is < 0 or > 5)
        {
            problems.Add(Msg.Of("product.cmd.badRetry", label, c.Retry));
        }
        CheckLights(c, label, machine, problems);

        JobInspection? job = CheckJobFile(c, label, paths, jobs, problems);
        CheckExtraCameras(c, label, machine, job, calibrations, problems);
        CheckOutputs(c, label, job, problems);
        CheckTunables(c, label, job, problems);
    }

    private void CheckCalibration(string id, string label, MachineView machine,
        Dictionary<string, ICalibration> calibrations, List<Msg> problems)
    {
        if (id.Length > 0 && !machine.CalibrationExists(id))
        {
            problems.Add(Msg.Of("product.cmd.noCalibration", label, id));
        }
        else if (id.Length > 0 && !calibrations.ContainsKey(id))
        {
            // Có thư mục chưa đủ: mở thật, tool phải đúng kiểu và ĐÃ hiệu chuẩn (Chương 20).
            CalibrationLoad calibration = _calibrationLoader.Load(id);
            if (calibration.Calibration is null)
            {
                problems.Add(Msg.Of("product.cmd.calibrationUnusable", label,
                    calibration.Error ?? Msg.Empty));
            }
            else
            {
                calibrations[id] = calibration.Calibration;
            }
        }
    }

    private static void CheckCapture(string label, double? exposureMs, double? brightness,
        double? contrast, List<Msg> problems)
    {
        if (exposureMs is <= 0)
        {
            problems.Add(Msg.Of("product.cmd.badExposure", label, exposureMs));
        }
        // Brightness/Contrast của VisionPro là tỉ lệ 0–1 trên dải của camera.
        if (brightness is < 0 or > 1)
        {
            problems.Add(Msg.Of("product.cmd.badCameraParam", label, "brightness", brightness));
        }
        if (contrast is < 0 or > 1)
        {
            problems.Add(Msg.Of("product.cmd.badCameraParam", label, "contrast", contrast));
        }
    }

    /// <summary>
    /// Camera thêm: vai trò có thật, không trùng camera chính và không trùng nhau; terminal input có
    /// thật, kiểu ảnh, khác <c>InputImage</c>.
    /// </summary>
    private void CheckExtraCameras(CommandConfig c, string label, MachineView machine,
        JobInspection? job, Dictionary<string, ICalibration> calibrations, List<Msg> problems)
    {
        HashSet<string> cameras = new(StringComparer.OrdinalIgnoreCase) { c.Camera };
        HashSet<string> terminals = new(StringComparer.Ordinal) { InputImageTerminal };
        foreach (CameraInput extra in c.ExtraCameras)
        {
            if (!machine.Cameras.Contains(extra.Camera))
            {
                problems.Add(Msg.Of("product.cmd.noCamera", label, extra.Camera,
                    string.Join(", ", machine.Cameras)));
            }
            else if (!cameras.Add(extra.Camera))
            {
                problems.Add(Msg.Of("product.cmd.cameraTwice", label, extra.Camera));
            }
            if (!terminals.Add(extra.Terminal))
            {
                problems.Add(Msg.Of("product.cmd.extraTerminal", label, extra.Terminal));
            }
            else if (job is not null)
            {
                TerminalInfo? terminal = job.Inputs.FirstOrDefault(t => t.Name == extra.Terminal);
                if (terminal is null || !IsImageType(terminal.ValueType))
                {
                    problems.Add(Msg.Of("product.cmd.extraTerminal", label, extra.Terminal));
                }
            }
            CheckCalibration(extra.Calibration, label, machine, calibrations, problems);
            CheckCapture(label, extra.ExposureMs, extra.Brightness, extra.Contrast, problems);
        }
    }

    // App không biết kiểu Cognex: terminal ảnh là kiểu có tên "...Image" (CogImage8Grey, ICogImage)
    // hoặc object.
    private static bool IsImageType(Type type) =>
        type == typeof(object) || type.Name.EndsWith("Image", StringComparison.Ordinal);

    private static void CheckLights(CommandConfig c, string label, MachineView machine,
        List<Msg> problems)
    {
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        foreach (LightSetting light in c.Lights)
        {
            if (!machine.Lights.TryGetValue(light.Role, out var limits))
            {
                problems.Add(Msg.Of("product.cmd.noLight", label, light.Role));
                continue;
            }
            if (light.Channel < 1 || light.Channel > limits.Channels)
            {
                problems.Add(Msg.Of("product.cmd.badLightChannel", label, light.Role,
                    light.Channel, limits.Channels));
            }
            if (light.Intensity < 0 || light.Intensity > limits.MaxIntensity)
            {
                problems.Add(Msg.Of("product.cmd.badIntensity", label, light.Role,
                    light.Intensity, limits.MaxIntensity));
            }
            if (!seen.Add(light.Role + "/" + light.Channel))
            {
                problems.Add(Msg.Of("product.cmd.lightTwice", label, light.Role, light.Channel));
            }
            if (light.SettleMs is < 0 or > MaxSettleMs)
            {
                problems.Add(Msg.Of("product.cmd.badSettle", label, light.Role, light.SettleMs,
                    MaxSettleMs));
            }
        }
    }

    private JobInspection? CheckJobFile(CommandConfig c, string label, ProductPaths paths,
        Dictionary<string, JobInspection> jobs, List<Msg> problems)
    {
        // Chỉ tên file: đường dẫn tuyệt đối hay "..\" làm thư mục sản phẩm hết tự chứa — chép
        // sang máy khác là trỏ vào file không có ở đó.
        if (c.Job.Trim().Length == 0 || c.Job.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            !c.Job.EndsWith(".vpp", StringComparison.OrdinalIgnoreCase))
        {
            problems.Add(Msg.Of("product.cmd.badJobName", label, c.Job));
            return null;
        }
        string file = paths.Job(c.Job);
        if (!File.Exists(file))
        {
            problems.Add(Msg.Of("product.cmd.jobMissing", label, file));
            return null;
        }

        if (!jobs.TryGetValue(c.Job, out JobInspection? job))
        {
            job = _inspector.Inspect(file);   // mỗi file chỉ mở một lần dù nhiều lệnh dùng chung
            jobs[c.Job] = job;
        }
        if (job.LoadError is not null)
        {
            problems.Add(Msg.Of("product.cmd.jobUnusable", label, c.Job, job.LoadError));
            return null;
        }
        if (job.ScriptError is not null)
        {
            problems.Add(Msg.Of("product.cmd.jobScript", label, c.Job, job.ScriptError));
        }
        if (!job.Inputs.Any(t => t.Name == InputImageTerminal))
        {
            problems.Add(Msg.Of("product.cmd.noInputImage", label, c.Job, InputImageTerminal));
        }
        return job;
    }

    private static void CheckOutputs(CommandConfig c, string label, JobInspection? job,
        List<Msg> problems)
    {
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (OutputConfig output in c.Outputs)
        {
            if (output.Terminal.Trim().Length == 0)
            {
                problems.Add(Msg.Of("product.out.terminalEmpty", label));
                continue;
            }
            if (!seen.Add(output.Terminal))
            {
                problems.Add(Msg.Of("product.out.duplicate", label, output.Terminal));
            }
            if (output.Lower > output.Upper)
            {
                problems.Add(Msg.Of("product.out.limits", label, output.Terminal, output.Lower,
                    output.Upper));
            }
            if (!IsValidFormat(output.Format))
            {
                problems.Add(Msg.Of("product.out.badFormat", label, output.Terminal,
                    output.Format));
            }
            if (job is null)
            {
                continue;   // file job hỏng đã báo ở trên — không báo thêm lỗi dây chuyền
            }

            // Tên terminal phân biệt hoa/thường như trong ToolBlock.
            TerminalInfo? terminal = job.Outputs.FirstOrDefault(t => t.Name == output.Terminal);
            if (terminal is null)
            {
                problems.Add(Msg.Of("product.out.noTerminal", label, output.Terminal, c.Job,
                    string.Join(", ", job.Outputs.Select(t => t.Name))));
            }
            else if ((output.Lower is not null || output.Upper is not null) &&
                     !NumericTypes.Contains(terminal.ValueType))
            {
                problems.Add(Msg.Of("product.out.notNumber", label, output.Terminal,
                    terminal.ValueType.Name));
            }
            else if (!SendableTypes.Contains(terminal.ValueType))
            {
                // Ví dụ CogTransform2DLinear: không có cách "in" ra bản tin cho máy (Chương 20).
                problems.Add(Msg.Of("product.out.badType", label, output.Terminal,
                    terminal.ValueType.Name));
            }
        }
    }

    /// <summary>
    /// Tham số nhanh (Chương 22): terminal INPUT có thật và là số, khoảng hợp lệ, giá trị nằm
    /// trong khoảng. Khoảng là chỗ dừng tay kỹ sư khi kéo thanh trượt lúc máy đang chạy.
    /// </summary>
    private static void CheckTunables(CommandConfig c, string label, JobInspection? job,
        List<Msg> problems)
    {
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (TunableConfig tune in c.Tunables)
        {
            if (tune.Terminal.Trim().Length == 0 || tune.Terminal == InputImageTerminal)
            {
                problems.Add(Msg.Of("product.tune.badTerminal", label, tune.Terminal));
                continue;
            }
            if (!seen.Add(tune.Terminal))
            {
                problems.Add(Msg.Of("product.tune.duplicate", label, tune.Terminal));
            }
            if (tune.Min > tune.Max || tune.Step <= 0)
            {
                problems.Add(Msg.Of("product.tune.range", label, tune.Terminal, tune.Min,
                    tune.Max, tune.Step));
            }
            else if (tune.Value < tune.Min || tune.Value > tune.Max)
            {
                problems.Add(Msg.Of("product.tune.outOfRange", label, tune.Terminal, tune.Value,
                    tune.Min, tune.Max));
            }
            if (job is null)
            {
                continue;
            }
            TerminalInfo? terminal = job.Inputs.FirstOrDefault(t => t.Name == tune.Terminal);
            if (terminal is null)
            {
                problems.Add(Msg.Of("product.tune.noTerminal", label, tune.Terminal, c.Job,
                    string.Join(", ", job.Inputs.Select(t => t.Name))));
            }
            else if (!NumericTypes.Contains(terminal.ValueType))
            {
                problems.Add(Msg.Of("product.tune.notNumber", label, tune.Terminal,
                    terminal.ValueType.Name));
            }
        }
    }

    private static bool IsValidFormat(string format)
    {
        if (format.Trim().Length == 0)
        {
            return false;
        }
        try
        {
            // "0.000" hay "F3" đều hợp lệ; chuỗi lạ thường ra kết quả không có chữ số.
            string text = 1.2345.ToString(format, CultureInfo.InvariantCulture);
            return text.Any(char.IsDigit);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static ProductLoadResult Fail(Msg problem) =>
        new(null, new[] { problem }, Array.Empty<Msg>(), 0);
}
