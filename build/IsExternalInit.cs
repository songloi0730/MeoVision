namespace System.Runtime.CompilerServices;

// net48 không có sẵn type này (chỉ .NET 5+ mới có) — trình biên dịch C# 9+ cần nó tồn tại (bất kỳ
// ai định nghĩa) để cho phép cú pháp record/init. Polyfill chuẩn cộng đồng dùng cho record trên
// .NET Framework, không phải code tự viết logic gì.
internal static class IsExternalInit
{
}
