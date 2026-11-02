using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using MeoVision.Core;

namespace MeoVision.App.Results;

/// <summary>Một dòng kết quả đọc lại từ CSV.</summary>
public sealed record ResultRow(DateTime Time, string Code, string Serial, string Status,
    string Error, IReadOnlyList<(string Name, string Value)> Values, long TotalMs, string Channel,
    string Product, string Version, string ImageFile);

/// <summary>Đọc lại kết quả đã ghi của một ngày (form Lịch sử, Chương 23, mục 23.5).</summary>
public sealed class ResultReader
{
    private static readonly string[] Fixed = { "time", "serial", "status", "error" };
    private static readonly string[] Tail =
        { "total_ms", "channel", "product", "version", "image" };

    private readonly string _resultsRoot;
    private readonly string _imagesRoot;

    public ResultReader(string resultsRoot, string imagesRoot)
    {
        Guard.ThrowIfNull(resultsRoot, nameof(resultsRoot));
        Guard.ThrowIfNull(imagesRoot, nameof(imagesRoot));
        _resultsRoot = resultsRoot;
        _imagesRoot = imagesRoot;
    }

    public IReadOnlyList<ResultRow> Read(DateTime day)
    {
        string folder = Path.Combine(_resultsRoot, day.ToString("yyyy-MM-dd",
            CultureInfo.InvariantCulture));
        if (!Directory.Exists(folder))
        {
            return Array.Empty<ResultRow>();
        }
        List<ResultRow> rows = new();
        foreach (string file in Directory.GetFiles(folder, "*.csv"))
        {
            string code = Path.GetFileNameWithoutExtension(file);
            int underscore = code.LastIndexOf('_');
            if (underscore > 0 && int.TryParse(code.Substring(underscore + 1), out _))
            {
                code = code.Substring(0, underscore);   // T3_2.csv → T3
            }
            List<string>? header = null;
            foreach (string line in ReadShared(file))
            {
                List<string> fields = Split(line);
                if (header is null)
                {
                    header = fields;
                    continue;
                }
                if (fields.Count != header.Count)
                {
                    continue;   // dòng ghi dở (mất điện) — bỏ qua dòng đó, không bỏ cả file
                }
                rows.Add(ToRow(code, header, fields));
            }
        }
        return rows.OrderBy(r => r.Time).ToList();
    }

    private ResultRow ToRow(string code, List<string> header, List<string> f)
    {
        int n = header.Count;
        List<(string, string)> values = new();
        for (int i = Fixed.Length; i < n - Tail.Length; i++)
        {
            values.Add((header[i], f[i]));
        }
        DateTime.TryParseExact(f[0], "yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out DateTime time);
        long.TryParse(f[n - 5], NumberStyles.Integer, CultureInfo.InvariantCulture, out long ms);
        string image = f[n - 1].Length == 0 ? string.Empty
            : Path.Combine(_imagesRoot, f[n - 1].Replace('/', Path.DirectorySeparatorChar));
        return new ResultRow(time, code, f[1], f[2], f[3], values, ms, f[n - 4], f[n - 3],
            f[n - 2], image);
    }

    /// <summary>
    /// Đọc mà KHÔNG chặn người ghi (FileShare.ReadWrite): mở lịch sử đúng lúc bộ ghi đang ghi thì
    /// bộ ghi vẫn ghi được (đo thật: đọc kiểu mặc định của File.ReadLines làm mất một dòng kết
    /// quả).
    /// </summary>
    internal static IEnumerable<string> ReadShared(string file)
    {
        using FileStream stream = new(file, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using StreamReader reader = new(stream, Encoding.UTF8);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            yield return line;
        }
    }

    internal static string? FirstLine(string file) => ReadShared(file).FirstOrDefault();

    /// <summary>Tách một dòng CSV (RFC 4180: trường có dấu phẩy/ngoặc kép nằm trong ngoặc kép).</summary>
    internal static List<string> Split(string line)
    {
        List<string> fields = new();
        StringBuilder current = new();
        bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    quoted = false;
                }
                else
                {
                    current.Append(c);
                }
            }
            else if (c == '"')
            {
                quoted = true;
            }
            else if (c == ',')
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }
        fields.Add(current.ToString());
        return fields;
    }
}
