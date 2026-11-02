using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using MeoVision.App.Hosting;
using MeoVision.Core;
using MeoVision.Core.Localization;
using Newtonsoft.Json;

namespace MeoVision.App.Products;

/// <summary>Một file trong bản sao lưu: đường dẫn tương đối trong thư mục sản phẩm + mã băm.</summary>
public sealed record SnapshotFile(string Path, string Sha256, long Size);

/// <summary>Một bản sao lưu của sản phẩm: ai, lúc nào, vì sao, gồm những file nào.</summary>
public sealed record ProductSnapshot(string Name, string ProductId, DateTime CreatedUtc,
    string Reason, string User, IReadOnlyList<SnapshotFile> Files);

/// <summary>Kết quả khôi phục: thành công, hoặc lý do — khi lỗi thì sản phẩm KHÔNG bị đụng tới.</summary>
public sealed record RestoreResult(bool Ok, Msg Message);

/// <summary>
/// Sao lưu sản phẩm trước MỌI lần ghi đè (Chương 19, mục 19.5). Cách lưu: mỗi nội dung file chỉ
/// lưu MỘT lần dưới tên là mã SHA-256 của nó (<c>blobs</c>); mỗi bản sao lưu chỉ là danh sách
/// "đường dẫn → mã băm" (<c>snapshots/*.json</c>). Sửa một con số trong product.json thì bản sao
/// lưu mới chỉ tốn vài KB, không chép lại các file .vpp hàng chục MB. Mã băm còn dùng để phát hiện
/// bản sao lưu bị hỏng TRƯỚC khi khôi phục.
/// </summary>
public sealed class ProductBackup
{
    /// <summary>Thư mục con của sản phẩm được sao lưu. <c>samples</c> là ảnh thử — không sao lưu.</summary>
    public static readonly string[] TrackedFolders = { "jobs", "golden" };

    private static readonly JsonSerializerSettings JsonSettings = new()
    {
        Formatting = Formatting.Indented,
    };

    private readonly string _root;

    public ProductBackup(AppPaths paths)
    {
        Guard.ThrowIfNull(paths, nameof(paths));
        _root = Path.Combine(paths.Backup, "products");
    }

    /// <summary>Chụp trạng thái hiện tại của sản phẩm. Gọi TRƯỚC khi ghi đè bất kỳ file nào.</summary>
    public ProductSnapshot Create(ProductPaths product, string productId, string reason,
        string user)
    {
        Guard.ThrowIfNull(product, nameof(product));
        Guard.ThrowIfNull(productId, nameof(productId));
        string blobs = Path.Combine(_root, productId, "blobs");
        string snapshots = Path.Combine(_root, productId, "snapshots");
        Directory.CreateDirectory(blobs);
        Directory.CreateDirectory(snapshots);

        List<SnapshotFile> files = new();
        foreach (string file in TrackedFiles(product))
        {
            string hash = Sha256Of(file);
            string blob = Path.Combine(blobs, hash);
            if (!File.Exists(blob))
            {
                CopyAtomically(file, blob);   // nội dung đã có thì không chép lại
            }
            files.Add(new SnapshotFile(Relative(product.Directory, file), hash,
                new FileInfo(file).Length));
        }

        DateTime now = DateTime.UtcNow;
        string name = now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture) + "_" +
                      Safe(reason);
        ProductSnapshot snapshot = new(name, productId, now, reason, user, files);
        File.WriteAllText(Path.Combine(snapshots, name + ".json"),
            JsonConvert.SerializeObject(snapshot, JsonSettings), Encoding.UTF8);
        return snapshot;
    }

    /// <summary>Các bản sao lưu của một sản phẩm, mới nhất trước.</summary>
    public IReadOnlyList<ProductSnapshot> List(string productId)
    {
        string snapshots = Path.Combine(_root, productId, "snapshots");
        if (!Directory.Exists(snapshots))
        {
            return Array.Empty<ProductSnapshot>();
        }
        return Directory.GetFiles(snapshots, "*.json")
            .Select(f => JsonConvert.DeserializeObject<ProductSnapshot>(
                File.ReadAllText(f, Encoding.UTF8))!)
            .OrderByDescending(s => s.Name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Khôi phục sản phẩm về một bản sao lưu. Thứ tự: (1) kiểm mọi file của bản sao lưu còn nguyên
    /// — hỏng một file là dừng, không đụng gì; (2) sao lưu trạng thái hiện tại (để còn quay lại);
    /// (3) ghi đè, xoá file không có trong bản sao lưu ở các thư mục được theo dõi.
    /// </summary>
    public RestoreResult Restore(ProductPaths product, string productId, string snapshotName,
        string user)
    {
        Guard.ThrowIfNull(product, nameof(product));
        ProductSnapshot? snapshot = List(productId).FirstOrDefault(s => s.Name == snapshotName);
        if (snapshot is null)
        {
            return new RestoreResult(false, Msg.Of("backup.notFound", snapshotName));
        }

        string blobs = Path.Combine(_root, productId, "blobs");
        foreach (SnapshotFile file in snapshot.Files)
        {
            string blob = Path.Combine(blobs, file.Sha256);
            if (!File.Exists(blob) || Sha256Of(blob) != file.Sha256)
            {
                return new RestoreResult(false, Msg.Of("backup.corrupt", snapshotName, file.Path));
            }
        }

        Create(product, productId, "before-restore " + snapshotName, user);
        HashSet<string> keep = new(snapshot.Files.Select(f => f.Path),
            StringComparer.OrdinalIgnoreCase);
        foreach (string existing in TrackedFiles(product))
        {
            if (!keep.Contains(Relative(product.Directory, existing)))
            {
                File.Delete(existing);
            }
        }
        foreach (SnapshotFile file in snapshot.Files)
        {
            string target = Path.Combine(product.Directory, file.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            CopyAtomically(Path.Combine(blobs, file.Sha256), target);
        }
        return new RestoreResult(true, Msg.Of("backup.restored", snapshotName,
            snapshot.Files.Count));
    }

    /// <summary>
    /// Giữ <paramref name="keep"/> bản mới nhất; xoá blob không còn bản sao lưu nào dùng. Trả về số
    /// bản đã xoá.
    /// </summary>
    public int Prune(string productId, int keep)
    {
        if (keep < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(keep), keep, "keep >= 1");
        }
        IReadOnlyList<ProductSnapshot> all = List(productId);
        string snapshots = Path.Combine(_root, productId, "snapshots");
        foreach (ProductSnapshot old in all.Skip(keep))
        {
            File.Delete(Path.Combine(snapshots, old.Name + ".json"));
        }

        HashSet<string> used = new(all.Take(keep).SelectMany(s => s.Files).Select(f => f.Sha256),
            StringComparer.OrdinalIgnoreCase);
        string blobs = Path.Combine(_root, productId, "blobs");
        if (Directory.Exists(blobs))
        {
            foreach (string blob in Directory.GetFiles(blobs))
            {
                if (!used.Contains(Path.GetFileName(blob)))
                {
                    File.Delete(blob);
                }
            }
        }
        return Math.Max(0, all.Count - keep);
    }

    /// <summary>
    /// Phiên bản nội dung của sản phẩm (Chương 23): 12 ký tự đầu của SHA-256 trên các dòng
    /// "đường dẫn tương đối:mã băm" của đúng các file mà bản sao lưu chụp lại.
    /// </summary>
    public static string ContentVersion(ProductPaths product)
    {
        Guard.ThrowIfNull(product, nameof(product));
        StringBuilder list = new();
        foreach (string file in TrackedFiles(product))
        {
            list.Append(Relative(product.Directory, file)).Append(':').Append(Sha256Of(file))
                .Append('\n');
        }
        using SHA256 sha = SHA256.Create();
        byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(list.ToString()));
        return string.Concat(hash.Take(6).Select(b => b.ToString("x2",
            CultureInfo.InvariantCulture)));
    }

    private static IEnumerable<string> TrackedFiles(ProductPaths product)
    {
        if (File.Exists(product.ProductFile))
        {
            yield return product.ProductFile;
        }
        foreach (string folder in TrackedFolders)
        {
            string directory = Path.Combine(product.Directory, folder);
            if (!Directory.Exists(directory))
            {
                continue;
            }
            foreach (string file in Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
                         .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                yield return file;
            }
        }
    }

    /// <summary>Ghi ra file tạm rồi đổi tên: mất điện giữa chừng không để lại file ghi dở.</summary>
    internal static void CopyAtomically(string source, string target)
    {
        string temp = target + ".tmp";
        File.Copy(source, temp, overwrite: true);
        if (File.Exists(target))
        {
            File.Replace(temp, target, null);
        }
        else
        {
            File.Move(temp, target);
        }
    }

    internal static string Sha256Of(string file)
    {
        using SHA256 sha = SHA256.Create();
        using FileStream stream = File.OpenRead(file);
        return string.Concat(sha.ComputeHash(stream).Select(b => b.ToString("x2",
            CultureInfo.InvariantCulture)));
    }

    internal static string Relative(string root, string file) =>
        file.Substring(root.TrimEnd('\\', '/').Length + 1).Replace('\\', '/');

    private static string Safe(string text)
    {
        string cleaned = new(text.Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray());
        return cleaned.Length > 40 ? cleaned.Substring(0, 40) : cleaned;
    }
}
