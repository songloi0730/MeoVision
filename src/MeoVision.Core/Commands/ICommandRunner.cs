using System.Threading;
using System.Threading.Tasks;

namespace MeoVision.Core.Commands;

/// <summary>
/// Chạy một lệnh của bảng lệnh và trả kết quả có kiểu (Chương 20). Lớp giao tiếp (Chương 21) chỉ
/// biết interface này — test được bằng bản giả, không cần camera hay VisionPro.
/// </summary>
public interface ICommandRunner
{
    /// <param name="serial">Serial máy gửi kèm — chỉ để truy vết (Chương 23).</param>
    Task<CommandResult> ExecuteAsync(string channel, string code, string serial = "",
        CancellationToken ct = default);
}
