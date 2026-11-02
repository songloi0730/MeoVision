using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MeoVision.Core;
using Newtonsoft.Json;

namespace MeoVision.App.Results;

/// <summary>
/// Bộ đếm OK/NG/lỗi theo lệnh, giữ qua các lần mở máy (như <c>[Count] PASS/FAIL</c> trong cấu hình
/// của chương trình tham khảo): tắt máy giữa ca không làm mất số của ca. File <c>counters.json</c>
/// trong thư mục dữ liệu, ghi file tạm rồi đổi tên — mất điện giữa chừng không để lại file hỏng.
/// </summary>
public sealed class CounterStore
{
    private readonly string _file;
    private readonly object _sync = new();

    public CounterStore(string file)
    {
        Guard.ThrowIfNull(file, nameof(file));
        _file = file;
    }

    /// <summary>Mã lệnh → [OK, NG, lỗi]. Không có file hoặc file hỏng: bộ đếm rỗng.</summary>
    public Dictionary<string, int[]> Load()
    {
        Dictionary<string, int[]> counts = new(StringComparer.OrdinalIgnoreCase);
        lock (_sync)
        {
            if (!File.Exists(_file))
            {
                return counts;
            }
            try
            {
                Dictionary<string, int[]>? saved = JsonConvert.DeserializeObject<
                    Dictionary<string, int[]>>(File.ReadAllText(_file, Encoding.UTF8));
                foreach (KeyValuePair<string, int[]> pair in saved ?? new())
                {
                    if (pair.Value is { Length: 3 })
                    {
                        counts[pair.Key] = pair.Value;
                    }
                }
            }
            catch (JsonException)
            {
                // Bộ đếm hỏng không được chặn khởi động — đếm lại từ 0.
            }
        }
        return counts;
    }

    public void Save(IReadOnlyDictionary<string, int[]> counts)
    {
        Guard.ThrowIfNull(counts, nameof(counts));
        lock (_sync)
        {
            string temp = _file + ".tmp";
            File.WriteAllText(temp, JsonConvert.SerializeObject(counts, Formatting.Indented),
                new UTF8Encoding(false));
            if (File.Exists(_file))
            {
                File.Replace(temp, _file, null);
            }
            else
            {
                File.Move(temp, _file);
            }
        }
    }
}
