using System;
using System.Collections.Generic;
using System.IO;
using MeoVision.Core;

namespace MeoVision.Application.Hosting;

/// <summary>
/// Bố cục thư mục DỮ LIỆU của trạm (Chương 17, mục 17.3). Tách hẳn khỏi thư mục chương trình:
/// nâng cấp phần mềm = thay thư mục chương trình, dữ liệu sản xuất ở nguyên chỗ cũ.
/// </summary>
public sealed class AppPaths
{
    private AppPaths(string root)
    {
        Root = root;
        Projects = Path.Combine(root, "projects");
        Logs = Path.Combine(root, "logs");
        Images = Path.Combine(root, "images");
        Results = Path.Combine(root, "results");
        Backup = Path.Combine(root, "backup");
        Audit = Path.Combine(root, "audit");
        Diagnostics = Path.Combine(root, "diagnostics");
        UsersFile = Path.Combine(root, "users.json");
        ActiveProjectFile = Path.Combine(root, "active-project.txt");
    }

    public string Root { get; }
    public string Projects { get; }      // mỗi project (sản phẩm/máy) một thư mục con
    public string Logs { get; }          // log ứng dụng, xoay vòng theo ngày
    public string Images { get; }        // ảnh OK/NG theo ngày/lệnh
    public string Results { get; }       // CSV kết quả theo ngày
    public string Backup { get; }        // bản cũ của .vpp/cấu hình trước mỗi lần ghi đè
    public string Audit { get; }         // ai đổi tham số gì, lúc nào
    public string Diagnostics { get; }   // gói chẩn đoán (Chương 16, mục 16.4.5)
    public string UsersFile { get; }
    public string ActiveProjectFile { get; }

    /// <summary>
    /// <paramref name="configuredRoot"/> có thể tuyệt đối ("D:\MeoVisionData") hoặc tương đối;
    /// tương đối thì tính từ thư mục chương trình — chỉ nên dùng khi chạy thử trên máy dev.
    /// Hỗ trợ biến môi trường, ví dụ "%ProgramData%\MeoVision".
    /// </summary>
    public static AppPaths Resolve(string configuredRoot, string programDirectory)
    {
        Guard.ThrowIfNull(configuredRoot, nameof(configuredRoot));
        Guard.ThrowIfNull(programDirectory, nameof(programDirectory));
        if (configuredRoot.Trim().Length == 0)
        {
            throw new ArgumentException("DataRoot khong duoc rong", nameof(configuredRoot));
        }

        string expanded = Environment.ExpandEnvironmentVariables(configuredRoot.Trim());
        string root = Path.IsPathRooted(expanded)
            ? expanded
            : Path.Combine(programDirectory, expanded);
        return new AppPaths(Path.GetFullPath(root));
    }

    public IReadOnlyList<string> AllDirectories => new[]
    {
        Root, Projects, Logs, Images, Results, Backup, Audit, Diagnostics,
    };

    /// <summary>Tạo mọi thư mục còn thiếu. Gọi đúng một lần, ở bước đầu của khởi động.</summary>
    public void EnsureCreated()
    {
        foreach (string directory in AllDirectories)
        {
            Directory.CreateDirectory(directory);
        }
    }

    public string ProjectDirectory(string projectName) =>
        Path.Combine(Projects, projectName);
}
