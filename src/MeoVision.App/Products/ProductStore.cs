using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MeoVision.App.Hosting;
using MeoVision.Core;
using MeoVision.Core.Localization;
using MeoVision.Core.Products;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace MeoVision.App.Products;

/// <summary>Đã đổi sản phẩm: trước/sau, phiên bản, ai yêu cầu (Chương 23).</summary>
public sealed class ProductSwitchedEventArgs : EventArgs
{
    public ProductSwitchedEventArgs(string previous, string previousVersion, string product,
        string version, string requestedBy)
    {
        Previous = previous;
        PreviousVersion = previousVersion;
        Product = product;
        Version = version;
        RequestedBy = requestedBy;
    }

    public string Previous { get; }

    public string PreviousVersion { get; }

    public string Product { get; }

    public string Version { get; }

    public string RequestedBy { get; }
}

/// <summary>
/// Kết quả áp dụng / quay lại: thành công kèm tên bản sao lưu TRƯỚC thay đổi (để quay lại một
/// chạm), hoặc lý do — khi đó sản phẩm đang chạy và file trên đĩa giữ nguyên như trước.
/// </summary>
public sealed record ApplyResult(bool Ok, string? PreviousSnapshot, IReadOnlyList<Msg> Problems)
{
    public static ApplyResult Failed(Msg problem) => new(false, null, new[] { problem });
}

/// <summary>
/// Sản phẩm đang chạy và các thao tác trên thư mục sản phẩm (Chương 19, mục 19.6): liệt kê, nạp sản
/// phẩm đang chọn, CHUYỂN sản phẩm theo kiểu "kiểm xong mới đổi", lưu có sao lưu trước. Quyền
/// (ai được chuyển) do Chương 23 chặn ở tầng gọi; ở đây chỉ ghi lại ai yêu cầu.
/// </summary>
public sealed class ProductStore
{
    // Ghi ra đúng kiểu người viết tay (camelCase, bỏ giá trị null, không BOM): product.json là file
    // kỹ sư mở bằng Notepad và so khác biệt — đo thật ở Chương 22: thiếu ba dòng này, mỗi lần áp
    // dụng biến "value" thành "Value" và thêm hàng chục dòng "lower": null.
    private static readonly JsonSerializerSettings JsonSettings = new()
    {
        Formatting = Formatting.Indented,
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        NullValueHandling = NullValueHandling.Ignore,
    };

    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

    private readonly AppPaths _paths;
    private readonly ProductLoader _loader;
    private readonly ProductBackup _backup;
    private readonly ILogger<ProductStore> _logger;
    private readonly object _sync = new();
    private readonly object _applySync = new();
    private LoadedProduct? _current;

    public ProductStore(AppPaths paths, ProductLoader loader, ProductBackup backup,
        ILogger<ProductStore> logger)
    {
        Guard.ThrowIfNull(paths, nameof(paths));
        Guard.ThrowIfNull(loader, nameof(loader));
        Guard.ThrowIfNull(backup, nameof(backup));
        Guard.ThrowIfNull(logger, nameof(logger));
        _paths = paths;
        _loader = loader;
        _backup = backup;
        _logger = logger;
    }

    /// <summary>Sản phẩm đã qua kiểm tra và đang chạy; null = chưa có sản phẩm chạy được.</summary>
    public ProductConfig? Current
    {
        get { lock (_sync) { return _current?.Config; } }
    }

    /// <summary>
    /// Mượn sản phẩm đang chạy cho MỘT lệnh (Chương 20): trong lúc mượn, chuyển sản phẩm không
    /// đóng job đang chạy dở. Null = chưa có sản phẩm chạy được. Nhớ Dispose lease khi xong.
    /// </summary>
    public (LoadedProduct Product, IDisposable Lease)? Acquire()
    {
        lock (_sync)
        {
            // Trong khoá: đọc và mượn là MỘT bước; Switch thay sản phẩm trong cùng khoá, nên sau
            // khi thay không ai mượn được bản cũ (bản cũ chỉ bị cho nghỉ sau khi đã thay).
            IDisposable? lease = _current?.Acquire();
            return lease is null ? null : (_current!, lease);
        }
    }

    public event EventHandler? CurrentChanged;

    /// <summary>
    /// Đã đổi sang sản phẩm (hoặc phiên bản) mới — kèm ai yêu cầu, để nhật ký thay đổi ghi lại cả
    /// lần đổi do máy gửi <c>SW</c> (Chương 23).
    /// </summary>
    public event EventHandler<ProductSwitchedEventArgs>? Switched;

    /// <summary>Phiên bản (mã băm nội dung) của sản phẩm đang chạy; rỗng nếu chưa có.</summary>
    public string CurrentVersion
    {
        get { lock (_sync) { return _current?.Version ?? string.Empty; } }
    }

    /// <summary>
    /// Dịch thông điệp cho LOG (ngôn ngữ cố định của log, Chương 17). Màn hình chỉ có chỗ cho lỗi
    /// đầu tiên; log ghi ĐỦ mọi lỗi để người sửa file thấy hết trong một lần.
    /// </summary>
    public Func<Msg, string> FormatForLog { get; set; } = m => m.ToString();

    /// <summary>Mã sản phẩm ghi trong <c>active-product.txt</c>; null nếu chưa chọn.</summary>
    public string? ActiveId
    {
        get
        {
            if (!File.Exists(_paths.ActiveProductFile))
            {
                return null;
            }
            string id = File.ReadAllText(_paths.ActiveProductFile, Encoding.UTF8).Trim();
            return id.Length == 0 ? null : id;
        }
    }

    /// <summary>Thư mục con của <c>products</c> có <c>product.json</c>.</summary>
    public IReadOnlyList<string> List() =>
        Directory.Exists(_paths.Products)
            ? Directory.GetDirectories(_paths.Products)
                .Where(d => File.Exists(Path.Combine(d, "product.json")))
                .Select(d => Path.GetFileName(d))
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList()
            : Array.Empty<string>();

    /// <summary>Nạp sản phẩm đang chọn lúc khởi động. Lỗi thì <see cref="Current"/> vẫn null.</summary>
    public ProductLoadResult? LoadActive(MachineView machine)
    {
        string? id = ActiveId;
        if (id is null)
        {
            return null;
        }
        ProductLoadResult result = _loader.Load(_paths.Product(id), id, machine);
        LogFindings(id, result);
        if (result.IsValid)
        {
            SetCurrent(result.Loaded!);
        }
        return result;
    }

    /// <summary>
    /// Chuyển sang sản phẩm khác. Nạp và kiểm TOÀN BỘ sản phẩm mới trước; chỉ khi hợp lệ mới ghi
    /// <c>active-product.txt</c> và thay <see cref="Current"/>. Lỗi thì sản phẩm cũ vẫn chạy nguyên —
    /// máy không bao giờ rơi vào trạng thái "nửa cũ nửa mới".
    /// </summary>
    public ProductLoadResult Switch(string productId, MachineView machine, string requestedBy)
    {
        Guard.ThrowIfNull(productId, nameof(productId));
        Guard.ThrowIfNull(requestedBy, nameof(requestedBy));
        ProductLoadResult result = _loader.Load(_paths.Product(productId), productId, machine);
        LogFindings(productId, result);
        if (!result.IsValid)
        {
            _logger.LogWarning("Switch to {Product} by {User} refused: {Count} problem(s)",
                productId, requestedBy, result.Problems.Count);
            return result;
        }

        WriteAtomically(_paths.ActiveProductFile, productId);
        string? previous = Current?.Id;
        string previousVersion = CurrentVersion;
        SetCurrent(result.Loaded!);
        _logger.LogInformation("Product switched {Old} -> {New} ({Version}) by {User}",
            previous ?? "-", productId, result.Loaded!.Version, requestedBy);
        Switched?.Invoke(this, new ProductSwitchedEventArgs(previous ?? string.Empty,
            previousVersion, productId, result.Loaded.Version, requestedBy));
        return result;
    }

    /// <summary>
    /// Ghi <c>product.json</c>: sao lưu trạng thái cũ trước, rồi ghi kiểu file tạm + đổi tên. Không
    /// tự đổi <see cref="Current"/> — muốn chạy bản mới thì gọi <see cref="Switch"/> để được kiểm
    /// lại.
    /// </summary>
    public ProductSnapshot Save(ProductConfig product, string user, string reason)
    {
        Guard.ThrowIfNull(product, nameof(product));
        ProductPaths paths = _paths.Product(product.Id);
        Directory.CreateDirectory(paths.Directory);
        ProductSnapshot snapshot = _backup.Create(paths, product.Id, reason, user);
        WriteAtomically(paths.ProductFile, JsonConvert.SerializeObject(product, JsonSettings));
        _logger.LogInformation("Product {Product} saved by {User} ({Reason}); backup {Snapshot}",
            product.Id, user, reason, snapshot.Name);
        return snapshot;
    }

    /// <summary>
    /// Bản nháp cũ bị bỏ dở thì xoá. Xoá không được thì dùng thư mục mới, KHÔNG làm hỏng việc mở
    /// Thiết lập lệnh — lỗi thật 2026-10-30: thư mục dữ liệu nằm trong OneDrive, OneDrive giữ thư mục
    /// <c>jobs</c> cũ (ReadOnly + ReparsePoint), xoá báo "Access denied" và form không mở được.
    /// </summary>
    private string FreshDraftFolder(string productId)
    {
        string folder = Path.Combine(_paths.Drafts, productId);
        if (!Directory.Exists(folder))
        {
            return folder;
        }
        try
        {
            Directory.Delete(folder, recursive: true);
            return folder;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            string fresh = folder + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff",
                System.Globalization.CultureInfo.InvariantCulture);
            _logger.LogWarning(ex, "Old draft {Folder} could not be removed; using {Fresh}", folder,
                fresh);
            return fresh;
        }
    }

    /// <summary>
    /// Mở bản nháp của sản phẩm đang chạy (Chương 22): chép product.json + jobs sang
    /// <c>drafts\&lt;mã&gt;</c> rồi nạp bằng chính bộ nạp — job nháp là đối tượng RIÊNG.
    /// </summary>
    public DraftOpenResult OpenDraft(MachineView machine, string user)
    {
        Guard.ThrowIfNull(machine, nameof(machine));
        Guard.ThrowIfNull(user, nameof(user));
        ProductConfig? running = Current;
        if (running is null)
        {
            return new DraftOpenResult(null, new[] { Msg.Of("cmd.noProduct") });
        }
        ProductPaths source = _paths.Product(running.Id);
        ProductPaths draft = new(FreshDraftFolder(running.Id));
        Directory.CreateDirectory(draft.Directory);
        File.Copy(source.ProductFile, draft.ProductFile);
        foreach (string folder in ProductBackup.TrackedFolders)
        {
            CopyFolder(Path.Combine(source.Directory, folder),
                Path.Combine(draft.Directory, folder));
        }

        ProductLoadResult result = _loader.Load(draft, running.Id, machine);
        if (!result.IsValid)
        {
            LogFindings(running.Id + " (draft)", result);
            return new DraftOpenResult(null, result.Problems);
        }
        _logger.LogInformation("Draft of {Product} opened by {User}", running.Id, user);
        return new DraftOpenResult(new DraftSession(draft, result.Loaded!, user),
            Array.Empty<Msg>());
    }

    /// <summary>
    /// Áp dụng bản nháp, nguyên khối, giữa hai lệnh: ghi job đã sửa + product.json vào thư mục
    /// nháp → sao lưu sản phẩm → chép sang → <see cref="Switch"/> (nạp và kiểm lại TỪ ĐĨA). Kiểm
    /// không qua thì khôi phục bản sao lưu vừa tạo; sản phẩm cũ chưa từng ngừng chạy.
    /// </summary>
    public ApplyResult Apply(DraftSession draft, string reason, MachineView machine)
    {
        Guard.ThrowIfNull(draft, nameof(draft));
        Guard.ThrowIfNull(reason, nameof(reason));
        Guard.ThrowIfNull(machine, nameof(machine));
        lock (_applySync)
        {
            if (!string.Equals(Current?.Id, draft.ProductId, StringComparison.OrdinalIgnoreCase))
            {
                return ApplyResult.Failed(Msg.Of("draft.productChanged", draft.ProductId,
                    Current?.Id ?? "-"));
            }
            ProductConfig config = draft.WriteFiles();
            ProductPaths target = _paths.Product(draft.ProductId);
            ProductSnapshot before = _backup.Create(target, draft.ProductId, "apply " + reason,
                draft.User);
            WriteAtomically(target.ProductFile, JsonConvert.SerializeObject(config, JsonSettings));
            foreach (string job in draft.EditedJobs)
            {
                ProductBackup.CopyAtomically(draft.Paths.Job(job), target.Job(job));
            }
            return SwitchOrRestore(draft.ProductId, before, machine, draft.User, reason);
        }
    }

    /// <summary>
    /// Quay lại một bản sao lưu (thường là bản tạo ngay trước lần áp dụng gần nhất). Bản hiện tại
    /// được sao lưu trước, nên quay lại cũng quay lại được.
    /// </summary>
    public ApplyResult RollBack(string productId, string snapshotName, MachineView machine,
        string user)
    {
        Guard.ThrowIfNull(productId, nameof(productId));
        Guard.ThrowIfNull(snapshotName, nameof(snapshotName));
        Guard.ThrowIfNull(machine, nameof(machine));
        Guard.ThrowIfNull(user, nameof(user));
        lock (_applySync)
        {
            ProductPaths target = _paths.Product(productId);
            ProductSnapshot before = _backup.Create(target, productId, "before rollback", user);
            RestoreResult restored = _backup.Restore(target, productId, snapshotName, user);
            if (!restored.Ok)
            {
                return ApplyResult.Failed(restored.Message);
            }
            return SwitchOrRestore(productId, before, machine, user, "rollback " + snapshotName);
        }
    }

    private ApplyResult SwitchOrRestore(string productId, ProductSnapshot before,
        MachineView machine, string user, string reason)
    {
        ProductLoadResult result = Switch(productId, machine, user);
        if (result.IsValid)
        {
            _logger.LogInformation("Product {Product} changed by {User} ({Reason}); previous " +
                                   "state in backup {Snapshot}", productId, user, reason,
                before.Name);
            return new ApplyResult(true, before.Name, Array.Empty<Msg>());
        }
        // File trên đĩa phải khớp sản phẩm đang chạy: trả lại đúng trạng thái trước.
        RestoreResult undo = _backup.Restore(_paths.Product(productId), productId, before.Name,
            user);
        if (!undo.Ok)
        {
            _logger.LogError("Product {Product}: restoring {Snapshot} after a failed change " +
                             "also failed: {Error}", productId, before.Name,
                FormatForLog(undo.Message));
        }
        return new ApplyResult(false, null, result.Problems);
    }

    private static void CopyFolder(string source, string target)
    {
        if (!Directory.Exists(source))
        {
            return;
        }
        foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            string copy = Path.Combine(target, file.Substring(source.Length + 1));
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            File.Copy(file, copy);
        }
    }

    private void LogFindings(string productId, ProductLoadResult result)
    {
        for (int i = 0; i < result.Problems.Count; i++)
        {
            _logger.LogError("Product {Product} problem {Index}/{Count}: {Text}", productId, i + 1,
                result.Problems.Count, FormatForLog(result.Problems[i]));
        }
        foreach (Msg warning in result.Warnings)
        {
            _logger.LogWarning("Product {Product}: {Text}", productId, FormatForLog(warning));
        }
    }

    /// <summary>
    /// Tắt máy: sản phẩm đang chạy thôi nhận lệnh, job và hiệu chuẩn được đóng (ngay, hoặc khi lệnh
    /// đang chạy dở trả lại).
    /// </summary>
    public void Close()
    {
        lock (_sync)
        {
            _current?.Retire();
            _current = null;
        }
    }

    private void SetCurrent(LoadedProduct product)
    {
        LoadedProduct? previous;
        lock (_sync)
        {
            previous = _current;
            _current = product;
        }
        // Sản phẩm cũ thôi nhận lệnh mới; job của nó đóng khi lệnh cuối đang chạy trả lại. Cho
        // nghỉ NGOÀI khoá: đóng một ToolBlock đã chạy hàng nghìn lần mất 45–140 ms (đo thật,
        // Chương 22) — trong khoá, mọi lệnh đọc Current/Acquire phải chờ (đo: một lệnh 542 ms).
        // Vẫn an toàn: sau khi thay, Acquire chỉ còn thấy sản phẩm mới; lệnh đã mượn bản cũ giữ
        // lease nên job cũ chưa bị đóng giữa chừng.
        previous?.Retire();
        CurrentChanged?.Invoke(this, EventArgs.Empty);
    }

    private static void WriteAtomically(string file, string content)
    {
        string temp = file + ".tmp";
        File.WriteAllText(temp, content, Utf8NoBom);
        if (File.Exists(file))
        {
            File.Replace(temp, file, null);
        }
        else
        {
            File.Move(temp, file);
        }
    }
}
