namespace System.Diagnostics.CodeAnalysis;

// net48 không có sẵn attribute này (chỉ .NET Core 3+) — cần để trình phân tích nullable hiểu
// Guard.ThrowIfNull đảm bảo tham số khác null sau khi gọi (giống [NotNull] trên
// ArgumentNullException.ThrowIfNull thật của .NET 6+). Chỉ cần khai báo 1 lần ở đây — khác
// IsExternalInit, attribute này đọc được qua reference bình thường, không cần lặp lại mỗi project.
[AttributeUsage(AttributeTargets.Parameter)]
internal sealed class NotNullAttribute : Attribute
{
}
