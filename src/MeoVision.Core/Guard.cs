using System;
using System.Diagnostics.CodeAnalysis;

namespace MeoVision.Core;

// net48 không có ArgumentNullException.ThrowIfNull (chỉ .NET 6+) — helper tương đương, yêu
// cầu truyền tên tham số rõ ràng thay vì dựa vào CallerArgumentExpression (cũng không có
// sẵn ở net48, và tường minh vẫn dễ đọc hơn cho code trích vào sách).
public static class Guard
{
    public static void ThrowIfNull([NotNull] object? argument, string paramName)
    {
        if (argument is null)
        {
            throw new ArgumentNullException(paramName);
        }
    }

    /// <summary>Như ThrowIfNull nhưng trả lại giá trị — dùng được trong lời gọi base(...).</summary>
    public static T NotNull<T>([NotNull] T? argument, string paramName) where T : class =>
        argument ?? throw new ArgumentNullException(paramName);
}
