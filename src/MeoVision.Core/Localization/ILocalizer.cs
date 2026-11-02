using System;
using System.Collections.Generic;

namespace MeoVision.Core.Localization;

/// <summary>Bộ dịch: khoá → chữ theo ngôn ngữ đang chọn (Chương 17, mục 17.6).</summary>
public interface ILocalizer
{
    /// <summary>Mã ngôn ngữ đang dùng, ví dụ "vi-VN".</summary>
    string Language { get; }

    /// <summary>Các ngôn ngữ có file dịch, theo thứ tự tên.</summary>
    IReadOnlyList<string> AvailableLanguages { get; }

    /// <summary>Dịch một khoá. Thiếu khoá: rơi về ngôn ngữ mặc định, rồi về "[khoá]".</summary>
    string Text(string key, params object[] args);

    /// <summary>Dịch một thông điệp (tham số là <see cref="Msg"/> cũng được dịch).</summary>
    string Text(Msg message);

    /// <summary>Chỉ trả chữ khi khoá CÓ trong ngôn ngữ đang chọn hoặc mặc định.</summary>
    bool TryText(string key, out string text);

    /// <summary>
    /// Tên ngôn ngữ viết BẰNG CHÍNH ngôn ngữ đó (khoá "language.name") — cho menu chọn.
    /// </summary>
    string DisplayName(string language);

    void SetLanguage(string language);

    event EventHandler? LanguageChanged;
}
