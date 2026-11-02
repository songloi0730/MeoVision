using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using MeoVision.Core;
using Newtonsoft.Json;

namespace MeoVision.App.Audit;

/// <summary>
/// Một sự kiện cần truy vết: ai (tên + vai trò), lúc nào, làm gì, trên sản phẩm nào, kèm dữ liệu.
/// <see cref="Action"/> là khoá tiếng Anh cố định (<c>login</c>, <c>apply</c>…) — lọc được bằng
/// máy.
/// </summary>
public sealed record AuditEntry(DateTime Time, string Machine, string User, string Role,
    string Action, string Product, string Detail, IReadOnlyDictionary<string, string> Data);

/// <summary>
/// Nhật ký thay đổi (Chương 23, mục 23.3): file <c>audit\yyyy-MM.jsonl</c>, mỗi dòng một JSON, chỉ
/// ghi thêm, ghi xong là xuống đĩa. Khác log ứng dụng: log để kỹ sư gỡ lỗi và xoay vòng theo ngày;
/// nhật ký thay đổi trả lời "ai đổi gì, lúc nào, vì sao" và giữ lâu.
/// </summary>
public sealed class AuditLog
{
    private static readonly JsonSerializerSettings JsonSettings = new()
    {
        Formatting = Formatting.None,
        DateFormatString = "yyyy-MM-ddTHH:mm:ss.fffzzz",
    };

    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

    private readonly string _folder;
    private readonly object _sync = new();

    public AuditLog(string folder)
    {
        Guard.ThrowIfNull(folder, nameof(folder));
        _folder = folder;
    }

    public event EventHandler<AuditEntry>? Written;

    public void Write(string user, string role, string action, string product, string detail,
        IReadOnlyDictionary<string, string>? data = null)
    {
        AuditEntry entry = new(DateTime.Now, Environment.MachineName, user, role, action, product,
            detail, data ?? new Dictionary<string, string>());
        string line = JsonConvert.SerializeObject(entry, JsonSettings) + "\n";
        byte[] bytes = Utf8NoBom.GetBytes(line);
        lock (_sync)
        {
            Directory.CreateDirectory(_folder);
            // Mở – ghi – đóng mỗi lần: vài chục sự kiện một ca, không đáng giữ file mở; mất điện
            // ngay sau đó thì dòng đã nằm trên đĩa. File bị khoá (ai đó mở bằng Excel) thì ghi sang
            // file kế trong tháng — như bộ ghi kết quả (mục 23.4): không mất dòng nào.
            for (int n = 1; ; n++)
            {
                try
                {
                    using FileStream stream = new(FileOf(entry.Time, n), FileMode.Append,
                        FileAccess.Write, FileShare.ReadWrite, 4096, FileOptions.WriteThrough);
                    stream.Write(bytes, 0, bytes.Length);
                    break;
                }
                catch (IOException) when (n < 5)
                {
                }
            }
        }
        Written?.Invoke(this, entry);
    }

    /// <summary>Các sự kiện trong khoảng thời gian, cũ trước. Dòng hỏng bị bỏ qua.</summary>
    public IReadOnlyList<AuditEntry> Read(DateTime from, DateTime to)
    {
        List<AuditEntry> entries = new();
        for (DateTime month = new(from.Year, from.Month, 1); month <= to;
             month = month.AddMonths(1))
        {
            if (!Directory.Exists(_folder))
            {
                break;
            }
            string prefix = month.ToString("yyyy-MM", CultureInfo.InvariantCulture);
            IEnumerable<string> lines = Directory.GetFiles(_folder, prefix + "*.jsonl")
                .SelectMany(Results.ResultReader.ReadShared).ToList();
            foreach (string line in lines.Where(l => l.Trim().Length > 0))
            {
                AuditEntry? entry;
                try
                {
                    entry = JsonConvert.DeserializeObject<AuditEntry>(line, JsonSettings);
                }
                catch (JsonException)
                {
                    continue;
                }
                if (entry is not null && entry.Time >= from && entry.Time <= to)
                {
                    entries.Add(entry);
                }
            }
        }
        return entries.OrderBy(e => e.Time).ToList();
    }

    private string FileOf(DateTime time, int n) =>
        Path.Combine(_folder, time.ToString("yyyy-MM", CultureInfo.InvariantCulture) +
                              (n == 1 ? string.Empty : "_" + n) + ".jsonl");
}
