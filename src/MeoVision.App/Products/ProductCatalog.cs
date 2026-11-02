using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MeoVision.App.Hosting;
using MeoVision.Core;
using MeoVision.Core.Products;
using Newtonsoft.Json;

namespace MeoVision.App.Products;

/// <summary>Một sản phẩm trong thư mục dữ liệu, đọc nhanh để liệt kê (không mở job).</summary>
public sealed record ProductEntry(string Id, string Name, int Commands, string Version,
    DateTime Modified, ProductConfig? Config, string Error);

/// <summary>
/// Danh mục sản phẩm cho form Sản phẩm (mockup v2.2): mỗi thư mục con của <c>products</c> có
/// <c>product.json</c> là một sản phẩm. Chỉ đọc product.json — mở job (vài giây mỗi file) chỉ làm
/// khi thật sự chuyển sang sản phẩm đó (bộ nạp của Chương 19 kiểm toàn bộ lúc ấy). File hỏng vẫn
/// hiện, kèm lỗi, để người dùng biết nó có ở đó.
/// </summary>
public static class ProductCatalog
{
    public static IReadOnlyList<ProductEntry> List(AppPaths paths)
    {
        Guard.ThrowIfNull(paths, nameof(paths));
        List<ProductEntry> entries = new();
        if (!Directory.Exists(paths.Products))
        {
            return entries;
        }
        foreach (string folder in Directory.GetDirectories(paths.Products)
                     .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            string id = Path.GetFileName(folder);
            ProductPaths product = paths.Product(id);
            if (!File.Exists(product.ProductFile))
            {
                continue;
            }
            DateTime modified = Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
                .Select(File.GetLastWriteTime).DefaultIfEmpty(DateTime.MinValue).Max();
            try
            {
                ProductConfig? config = JsonConvert.DeserializeObject<ProductConfig>(
                    File.ReadAllText(product.ProductFile, Encoding.UTF8));
                entries.Add(new ProductEntry(id, config?.Name ?? string.Empty,
                    config?.Commands.Count ?? 0, ProductBackup.ContentVersion(product), modified,
                    config, string.Empty));
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                entries.Add(new ProductEntry(id, string.Empty, 0, string.Empty, modified, null,
                    ex.Message));
            }
        }
        return entries;
    }
}
