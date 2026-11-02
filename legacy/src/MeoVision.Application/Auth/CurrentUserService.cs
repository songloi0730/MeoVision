using System;
using MeoVision.Core;

namespace MeoVision.Application.Auth;

// Phiên đăng nhập chỉ sống trong bộ nhớ (mất khi tắt app) — đúng thực tế 1 ca làm việc, không
// cần bền vững qua lần chạy. Mật khẩu Engineer đọc từ config (KHÔNG hardcode trong source).
public sealed class CurrentUserService : ICurrentUserService
{
    private readonly string _engineerPassword;

    public CurrentUserService(MeoVisionHostOptions options)
    {
        Guard.ThrowIfNull(options, nameof(options));
        _engineerPassword = options.EngineerPassword;
    }

    public UserLevel Level { get; private set; } = UserLevel.Operator;

    public event EventHandler? LevelChanged;

    public bool TryLoginEngineer(string password)
    {
        Guard.ThrowIfNull(password, nameof(password));

        if (!string.Equals(password, _engineerPassword, StringComparison.Ordinal))
        {
            return false;
        }

        Level = UserLevel.Engineer;
        LevelChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void LogoutToOperator()
    {
        Level = UserLevel.Operator;
        LevelChanged?.Invoke(this, EventArgs.Empty);
    }
}
