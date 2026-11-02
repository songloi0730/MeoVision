using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace MeoVision.App.Comm;

/// <summary>
/// Một bản tin đã tách: mã lệnh + các trường còn lại (đã bỏ khoảng trắng hai đầu).
/// </summary>
public sealed record Request(string Code, IReadOnlyList<string> Fields)
{
    /// <summary>
    /// Trường thứ <paramref name="index"/> sau mã lệnh (đếm từ 0); thiếu thì chuỗi rỗng.
    /// </summary>
    public string Field(int index) => index < Fields.Count ? Fields[index] : string.Empty;
}

/// <summary>
/// Bản tin chuỗi của MeoVision (Chương 21, mục 21.2), theo ba quy tắc của Chương 15 (mục 15.1.6):
/// tra NGUYÊN mã lệnh, lệnh mang mã sản phẩm, trả lời luôn đủ số trường.
/// <code>
/// Hỏi:     T1,P-1024,SN88213
/// Trả lời: T1,SN88213,OK,,12.418,-3.207
///          T1,SN88213,NG,,12.418,-9.990
///          T1,SN88213,ERR,,,
/// </code>
/// Trả lời = mã, khoá (serial / sản phẩm / mã hiệu chuẩn), trạng thái, trường lý do, rồi đúng N giá trị.
/// Từ DESIGN_LOG [29] trường lý do gửi máy LUÔN TRỐNG (giữ chỗ để PLC đọc theo vị trí không phải
/// sửa): máy chỉ cần kết quả; vì sao NG/LỖI nằm ở màn vision, log và CSV.
/// </summary>
public static class MessageProtocol
{
    public const string Ok = "OK";
    public const string Ng = "NG";
    public const string Err = "ERR";

    /// <summary>Không kiểm — lệnh đang bỏ qua có kiểm soát (DESIGN_LOG [28] D1). Máy KHÔNG được coi là OK.</summary>
    public const string Bypass = "BYP";

    /// <summary>Dài nhất cho một bản tin — chặn rác hoặc dữ liệu nhị phân gửi nhầm cổng.</summary>
    public const int MaxLength = 256;

    private static readonly Regex CodePattern = new("^[A-Z][A-Z0-9]{0,7}$", RegexOptions.Compiled);

    /// <summary>
    /// Tách một dòng (đã bỏ ký tự kết thúc). Chỉ nhận ASCII in được: bản tin đi qua PLC/MES, một
    /// ký tự lạ là chỗ đầu tiên vỡ mã hoá (cùng lý do cấu hình viết tiếng Anh, Chương 19).
    /// </summary>
    public static bool TryParse(string line, out Request? request, out string reason)
    {
        request = null;
        reason = string.Empty;
        string text = (line ?? string.Empty).Trim();
        if (text.Length == 0 || text.Length > MaxLength || text.Any(c => c < 0x20 || c > 0x7E))
        {
            reason = "BAD_FORMAT";
            return false;
        }
        string[] parts = text.Split(',').Select(p => p.Trim()).ToArray();
        // Mã lệnh phân biệt hoa/thường như trong bảng lệnh; "t1" không phải "T1".
        if (!CodePattern.IsMatch(parts[0]))
        {
            reason = "BAD_FORMAT";
            return false;
        }
        request = new Request(parts[0], parts.Skip(1).ToList());
        return true;
    }

    /// <summary>
    /// Ghép bản tin trả lời, luôn đúng <paramref name="valueCount"/> giá trị sau lý do.
    /// </summary>
    public static string Reply(string code, string key, string status, string reason,
        IEnumerable<string> values, int valueCount)
    {
        List<string> fields = new() { code, key, status, reason };
        List<string> list = values.Take(valueCount).ToList();
        while (list.Count < valueCount)
        {
            list.Add(string.Empty);   // thiếu giá trị (lỗi, NG không đo được) vẫn giữ chỗ
        }
        fields.AddRange(list);
        return string.Join(",", fields.Select(Clean));
    }

    /// <summary>
    /// Tách lý do khỏi một câu trả lời: (câu gửi máy với trường lý do để trống, lý do). Giá trị đã qua
    /// <see cref="Clean"/> nên không có dấu phẩy lạc — tách theo dấu phẩy là đúng vị trí.
    /// </summary>
    public static (string Wire, string Reason) WithoutReason(string reply)
    {
        string[] fields = (reply ?? string.Empty).Split(',');
        if (fields.Length < 4 || fields[3].Length == 0)
        {
            return (reply ?? string.Empty, string.Empty);
        }
        string reason = fields[3];
        fields[3] = string.Empty;
        return (string.Join(",", fields), reason);
    }

    /// <summary>
    /// Dấu phẩy hay xuống dòng trong một giá trị (mã đọc được) sẽ làm lệch trường phía máy.
    /// </summary>
    private static string Clean(string value)
    {
        StringBuilder sb = new(value.Length);
        foreach (char c in value)
        {
            sb.Append(c == ',' || c < 0x20 || c > 0x7E ? '_' : c);
        }
        return sb.ToString();
    }
}
