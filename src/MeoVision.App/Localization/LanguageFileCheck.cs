using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using MeoVision.Core;

namespace MeoVision.App.Localization;

/// <summary>
/// So các bảng ngôn ngữ với bảng gốc (Chương 17, mục 17.6): thiếu khoá, thừa khoá, lệch số
/// tham số <c>{0}</c>. Chạy trong unit test (chặn bản build lỗi) VÀ lúc khởi động (bắt file
/// người dịch sửa tại hiện trường).
/// </summary>
public static class LanguageFileCheck
{
    private static readonly Regex Placeholder = new(@"\{(\d+)(?:[:,][^}]*)?\}");

    public static IReadOnlyList<string> Compare(
        IReadOnlyDictionary<string, Dictionary<string, string>> tables, string referenceLanguage)
    {
        Guard.ThrowIfNull(tables, nameof(tables));
        List<string> problems = new();
        Dictionary<string, string> reference = tables[referenceLanguage];

        foreach (KeyValuePair<string, Dictionary<string, string>> table in tables)
        {
            if (table.Key == referenceLanguage)
            {
                continue;
            }

            foreach (string key in reference.Keys.Where(k => !table.Value.ContainsKey(k)))
            {
                problems.Add($"{table.Key}: missing '{key}'");
            }
            foreach (string key in table.Value.Keys.Where(k => !reference.ContainsKey(k)))
            {
                problems.Add($"{table.Key}: extra '{key}'");
            }
            foreach (string key in reference.Keys.Where(table.Value.ContainsKey))
            {
                if (!PlaceholdersOf(reference[key]).SetEquals(PlaceholdersOf(table.Value[key])))
                {
                    problems.Add($"{table.Key}: placeholders differ for '{key}'");
                }
            }
        }
        return problems;
    }

    private static HashSet<int> PlaceholdersOf(string text) =>
        new(Placeholder.Matches(text).Cast<Match>().Select(m => int.Parse(m.Groups[1].Value)));
}
