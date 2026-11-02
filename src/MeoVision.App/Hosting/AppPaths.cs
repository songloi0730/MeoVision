using System;
using System.Collections.Generic;
using System.IO;
using MeoVision.Core;
using MeoVision.Core.Localization;

namespace MeoVision.App.Hosting;

/// <summary>
/// Bố cục thư mục DỮ LIỆU của trạm (Chương 17, mục 17.3). Ba nguyên tắc: (1) dữ liệu gom trong
/// MỘT thư mục (mặc định <c>Data</c> cạnh exe) — chương trình, job, cấu hình đi cùng nhau, bản phát
/// hành không băm nó, chép bản mới không đè lên nó; ảnh/kết quả/log ra ổ khác được; (2) cái gì của MÁY NÀY (camera, đèn, cổng, hiệu chuẩn camera) nằm ở
/// <see cref="MachineFile"/> và <see cref="MachineCalibration"/>; (3) mỗi SẢN PHẨM là một
/// thư mục tự chứa trong <see cref="Products"/> — chép nguyên thư mục sang máy cùng loại là
/// dùng được.
/// </summary>
public sealed class AppPaths
{
    private AppPaths(string root, string? images, string? results, string? logs)
    {
        Root = root;
        MachineFile = Path.Combine(root, "machine.json");
        MachineCalibration = Path.Combine(root, "machine", "calibration");
        Products = Path.Combine(root, "products");
        ActiveProductFile = Path.Combine(root, "active-product.txt");
        Logs = logs ?? Path.Combine(root, "logs");
        Images = images ?? Path.Combine(root, "images");
        Results = results ?? Path.Combine(root, "results");
        Backup = Path.Combine(root, "backup");
        Drafts = Path.Combine(root, "drafts");
        Audit = Path.Combine(root, "audit");
        Diagnostics = Path.Combine(root, "diagnostics");
        UsersFile = Path.Combine(root, "users.json");
        UiLanguageFile = Path.Combine(root, "ui-language.txt");
        RunMarkerFile = Path.Combine(root, "running.marker");
    }

    public string Root { get; }
    public string MachineFile { get; }          // phần cứng + vai trò của MÁY NÀY (Chương 18)
    public string MachineCalibration { get; }   // hiệu chuẩn theo camera vật lý (Chương 7)
    public string Products { get; }             // mỗi sản phẩm một thư mục tự chứa (Chương 19)
    public string ActiveProductFile { get; }    // sản phẩm đang chạy
    public string Logs { get; private set; }                 // log ứng dụng, xoay vòng theo ngày
    public string Images { get; private set; }               // ảnh OK/NG theo ngày/lệnh
    public string Results { get; private set; }              // CSV kết quả theo ngày
    public string Backup { get; }               // bản cũ của .vpp/cấu hình trước mỗi lần ghi đè
    public string Drafts { get; }               // bản nháp sản phẩm đang sửa (Chương 22)
    public string Audit { get; }                // ai đổi tham số gì, lúc nào (Chương 23)
    public string Diagnostics { get; }          // file sự cố, gói chẩn đoán (Chương 16 §16.4.5)
    public string UsersFile { get; }
    public string UiLanguageFile { get; }       // ngôn ngữ giao diện chọn lần cuối
    public string RunMarkerFile { get; }        // còn tồn tại = lần trước tắt bất thường

    /// <summary>
    /// <paramref name="configuredRoot"/> có thể tuyệt đối ("D:\MeoVisionData") hoặc chứa biến môi
    /// trường ("%ProgramData%\MeoVision"); tương đối thì tính từ thư mục chương trình — chỉ
    /// nên dùng khi chạy thử trên máy dev.
    /// </summary>
    /// <param name="imagesRoot">Thư mục ảnh ở ổ khác (như <c>E:\Image</c>); rỗng = trong dữ liệu.</param>
    /// <param name="resultsRoot">Thư mục CSV kết quả ở ổ khác; rỗng = trong dữ liệu.</param>
    /// <param name="logsRoot">Thư mục log ở ổ khác; rỗng = trong dữ liệu.</param>
    public static AppPaths Resolve(string configuredRoot, string programDirectory,
        string? imagesRoot = null, string? resultsRoot = null, string? logsRoot = null)
    {
        Guard.ThrowIfNull(configuredRoot, nameof(configuredRoot));
        Guard.ThrowIfNull(programDirectory, nameof(programDirectory));
        if (configuredRoot.Trim().Length == 0)
        {
            throw new ArgumentException("DataRoot must not be empty", nameof(configuredRoot));
        }

        string expanded = Environment.ExpandEnvironmentVariables(configuredRoot.Trim());
        string root = Path.IsPathRooted(expanded)
            ? expanded
            : Path.Combine(programDirectory, expanded);
        return new AppPaths(Path.GetFullPath(root), Optional(imagesRoot, programDirectory),
            Optional(resultsRoot, programDirectory), Optional(logsRoot, programDirectory));
    }

    private static string? Optional(string? configured, string programDirectory)
    {
        if (configured is null || configured.Trim().Length == 0)
        {
            return null;
        }
        string expanded = Environment.ExpandEnvironmentVariables(configured.Trim());
        return Path.GetFullPath(Path.IsPathRooted(expanded)
            ? expanded
            : Path.Combine(programDirectory, expanded));
    }

    public IReadOnlyList<string> AllDirectories => new[]
    {
        Root, MachineCalibration, Products, Logs, Images, Results, Backup, Drafts, Audit,
        Diagnostics,
    };

    /// <summary>
    /// Thư mục ảnh/kết quả/log ở ổ khác không tạo được (ổ D: không có trên máy mới sau khi tháo ổ
    /// chương trình sang) — đang ghi tạm vào thư mục dữ liệu. Bước "Thư mục dữ liệu" báo vàng.
    /// </summary>
    public IReadOnlyList<Msg> FallBacks { get; private set; } = Array.Empty<Msg>();

    /// <summary>Tạo mọi thư mục còn thiếu. Gọi đúng một lần, ở bước đầu của khởi động.</summary>
    public void EnsureCreated()
    {
        // Ổ ngoài hỏng không được làm sập chương trình trước cả khi có log: ghi tạm vào dữ liệu.
        List<Msg> fallBacks = new();
        Images = CreateOrFallBack(Images, Path.Combine(Root, "images"), fallBacks);
        Results = CreateOrFallBack(Results, Path.Combine(Root, "results"), fallBacks);
        Logs = CreateOrFallBack(Logs, Path.Combine(Root, "logs"), fallBacks);
        FallBacks = fallBacks;
        foreach (string directory in AllDirectories)
        {
            Directory.CreateDirectory(directory);
        }
    }

    private static string CreateOrFallBack(string wanted, string fallBack, List<Msg> problems)
    {
        if (string.Equals(wanted, fallBack, StringComparison.OrdinalIgnoreCase))
        {
            return wanted;
        }
        try
        {
            Directory.CreateDirectory(wanted);
            return wanted;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                       NotSupportedException)
        {
            problems.Add(Msg.Of("startup.dataFolder.fallBack", wanted, fallBack, ex.Message));
            return fallBack;
        }
    }

    /// <summary>Đường dẫn bên trong thư mục của một sản phẩm.</summary>
    public ProductPaths Product(string productId)
    {
        Guard.ThrowIfNull(productId, nameof(productId));
        if (productId.Trim().Length == 0 ||
            productId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException($"Invalid product id '{productId}'", nameof(productId));
        }
        return new ProductPaths(Path.Combine(Products, productId));
    }
}

/// <summary>
/// Bố cục bên trong một thư mục sản phẩm. Thư mục này TỰ CHỨA: không tham chiếu đường dẫn
/// tuyệt đối ra ngoài, không chứa IP/cổng/serial của máy — để chép sang máy khác là dùng
/// được.
/// </summary>
public sealed class ProductPaths
{
    public ProductPaths(string directory)
    {
        Directory = directory;
        ProductFile = Path.Combine(directory, "product.json");
        Jobs = Path.Combine(directory, "jobs");
        Golden = Path.Combine(directory, "golden");
        Samples = Path.Combine(directory, "samples");
    }

    public string Directory { get; }
    public string ProductFile { get; }   // bảng lệnh, ngưỡng, vai trò thiết bị (Chương 19)
    public string Jobs { get; }          // *.vpp của sản phẩm
    public string Golden { get; }        // vị trí chuẩn (Chương 7 §7.7.1)
    public string Samples { get; }       // ảnh mẫu để chạy thử/replay (Chương 16 §16.3.4)

    public string Job(string fileName) => Path.Combine(Jobs, fileName);
}
