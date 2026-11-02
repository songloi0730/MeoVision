using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using MeoVision.Core;
using MeoVision.Core.Localization;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace MeoVision.App.Localization;

/// <summary>
/// Bộ dịch đọc <c>languages/&lt;culture&gt;.json</c> (Chương 17, mục 17.6). Mỗi file là một
/// bảng phẳng khoá → chữ, UTF-8; tên file là mã ngôn ngữ. Thêm ngôn ngữ = thêm một file,
/// không build lại.
/// </summary>
public sealed class JsonLocalizer : ILocalizer
{
    private readonly Dictionary<string, Dictionary<string, string>> _tables;
    private readonly string _fallbackLanguage;
    private readonly ILogger _logger;
    private readonly HashSet<string> _reportedMissing = new(StringComparer.Ordinal);
    private Dictionary<string, string> _current;
    private CultureInfo _culture;

    public JsonLocalizer(IReadOnlyDictionary<string, Dictionary<string, string>> tables,
        string language, string fallbackLanguage, ILogger logger)
    {
        Guard.ThrowIfNull(tables, nameof(tables));
        Guard.ThrowIfNull(logger, nameof(logger));
        if (!tables.ContainsKey(fallbackLanguage))
        {
            throw new ArgumentException($"No language file for fallback '{fallbackLanguage}'",
                nameof(fallbackLanguage));
        }

        _tables = new Dictionary<string, Dictionary<string, string>>(
            tables.ToDictionary(p => p.Key, p => p.Value), StringComparer.OrdinalIgnoreCase);
        _fallbackLanguage = fallbackLanguage;
        _logger = logger;
        Language = fallbackLanguage;
        _current = _tables[fallbackLanguage];
        _culture = CultureInfo.GetCultureInfo(fallbackLanguage);
        SetLanguage(language);
    }

    public event EventHandler? LanguageChanged;

    public string Language { get; private set; }

    public IReadOnlyList<string> AvailableLanguages =>
        _tables.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();

    /// <summary>
    /// Đọc mọi file *.json trong thư mục ngôn ngữ (thường là "languages" cạnh exe).
    /// </summary>
    public static JsonLocalizer FromDirectory(string directory, string language,
        string fallbackLanguage, ILogger logger) =>
        new(LoadTables(directory), language, fallbackLanguage, logger);

    /// <summary>Đọc bảng của từng ngôn ngữ: tên file (bỏ đuôi) là mã ngôn ngữ.</summary>
    public static Dictionary<string, Dictionary<string, string>> LoadTables(string directory)
    {
        Guard.ThrowIfNull(directory, nameof(directory));
        Dictionary<string, Dictionary<string, string>> tables =
            new(StringComparer.OrdinalIgnoreCase);
        foreach (string file in Directory.GetFiles(directory, "*.json"))
        {
            string json = File.ReadAllText(file, Encoding.UTF8);
            tables[Path.GetFileNameWithoutExtension(file)] =
                JsonConvert.DeserializeObject<Dictionary<string, string>>(json)
                ?? new Dictionary<string, string>();
        }
        return tables;
    }

    public void SetLanguage(string language)
    {
        if (!_tables.TryGetValue(language ?? string.Empty, out Dictionary<string, string>? table))
        {
            _logger.LogWarning("Language '{Language}' not found, keeping '{Current}'",
                language, Language);
            return;
        }

        Language = _tables.Keys.First(k =>
            string.Equals(k, language, StringComparison.OrdinalIgnoreCase));
        _current = table;
        _culture = CultureInfo.GetCultureInfo(Language);
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    public string DisplayName(string language) =>
        _tables.TryGetValue(language, out Dictionary<string, string>? table) &&
        table.TryGetValue("language.name", out string? name)
            ? name
            : language;

    public bool TryText(string key, out string text)
    {
        if (_current.TryGetValue(key, out string? value) ||
            _tables[_fallbackLanguage].TryGetValue(key, out value))
        {
            text = value;
            return true;
        }
        text = string.Empty;
        return false;
    }

    public string Text(Msg message)
    {
        Guard.ThrowIfNull(message, nameof(message));
        object[] args = message.Args.Select(a => a is Msg m ? Text(m) : a).ToArray();
        return Text(message.Key, args);
    }

    public string Text(string key, params object[] args)
    {
        Guard.ThrowIfNull(key, nameof(key));
        if (key.Length == 0)
        {
            return string.Empty;
        }

        if (!TryText(key, out string template))
        {
            if (_reportedMissing.Add(key))
            {
                _logger.LogWarning("Missing text key '{Key}' in '{Language}'", key, Language);
            }
            return "[" + key + "]";
        }

        if (args is null || args.Length == 0)
        {
            return template;
        }

        try
        {
            // Định dạng theo văn hoá của NGÔN NGỮ HIỂN THỊ: 42,8 GB (vi) khác 42.8 GB (en).
            return string.Format(_culture, template, args);
        }
        catch (FormatException ex)
        {
            // Bản dịch sai số tham số: hiện chữ gốc thay vì làm sập màn hình.
            _logger.LogWarning(ex, "Bad format for key '{Key}' in '{Language}'", key, Language);
            return template;
        }
    }
}
