using System;
using System.Collections.Generic;
using MeoVision.App.Audit;
using MeoVision.Core;
using MeoVision.Core.Security;

namespace MeoVision.App.Security;

/// <summary>
/// Ai đang đứng trước máy (Chương 23, mục 23.2). Mặc định là Vận hành — không cần mật khẩu, chạy
/// được ca sản xuất. Kỹ sư/Quản trị đăng nhập để sửa, và tự về Vận hành sau một khoảng không thao
/// tác: người đi ăn trưa không để lại một màn hình có quyền sửa job. Mọi lần đăng nhập, sai mật
/// khẩu, đăng xuất đều vào nhật ký thay đổi.
/// </summary>
public sealed class Session
{
    private readonly UserStore _users;
    private readonly AuditLog _audit;
    private readonly Func<DateTime> _clock;
    private readonly object _sync = new();
    private DateTime _lastInput;

    public Session(UserStore users, AuditLog audit, TimeSpan idleTimeout,
        Func<DateTime>? clock = null)
    {
        Guard.ThrowIfNull(users, nameof(users));
        Guard.ThrowIfNull(audit, nameof(audit));
        _users = users;
        _audit = audit;
        IdleTimeout = idleTimeout;
        _clock = clock ?? (() => DateTime.Now);
        _lastInput = _clock();
    }

    public string UserName { get; private set; } = UserStore.OperatorName;

    public UserRole Role { get; private set; } = UserRole.Operator;

    public TimeSpan IdleTimeout { get; }

    /// <summary>Tên sản phẩm đang chạy cho nhật ký — chương trình gắn vào lúc khởi động.</summary>
    public Func<string> ProductForAudit { get; set; } = () => string.Empty;

    public event EventHandler? Changed;

    public bool Can(Permission permission) => Permissions.Allows(Role, permission);

    /// <summary>Còn bao lâu nữa thì tự đăng xuất; null khi đang là Vận hành.</summary>
    public TimeSpan? RemainingBeforeLogout
    {
        get
        {
            lock (_sync)
            {
                if (Role == UserRole.Operator || IdleTimeout <= TimeSpan.Zero)
                {
                    return null;
                }
                TimeSpan left = IdleTimeout - (_clock() - _lastInput);
                return left < TimeSpan.Zero ? TimeSpan.Zero : left;
            }
        }
    }

    public bool Login(string name, string password)
    {
        Guard.ThrowIfNull(name, nameof(name));
        Guard.ThrowIfNull(password, nameof(password));
        UserRole? role = _users.Verify(name, password);
        if (role is null)
        {
            // Không khoá, không chờ: chỉ ghi lại. Ai thử nhiều lần sẽ hiện rõ trong nhật ký.
            Record("login-failed", "", new Dictionary<string, string> { ["name"] = name });
            return false;
        }
        lock (_sync)
        {
            UserName = name;
            Role = role.Value;
            _lastInput = _clock();
        }
        Record("login", role.Value.ToString());
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void Logout(string reason = "manual")
    {
        if (Role == UserRole.Operator)
        {
            return;
        }
        Record("logout", reason);
        lock (_sync)
        {
            UserName = UserStore.OperatorName;
            Role = UserRole.Operator;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Có thao tác chuột/phím — giao diện gọi.</summary>
    public void Touch()
    {
        lock (_sync)
        {
            _lastInput = _clock();
        }
    }

    /// <summary>Gọi định kỳ (mỗi giây): hết thời gian không thao tác thì về Vận hành.</summary>
    public void Tick()
    {
        if (RemainingBeforeLogout == TimeSpan.Zero)
        {
            Logout("idle");
        }
    }

    public void ChangePassword(string newPassword)
    {
        if (Role == UserRole.Operator)
        {
            throw new InvalidOperationException("Operator has no password");
        }
        _users.SetPassword(UserName, newPassword);
        Record("password-changed", string.Empty);
    }

    /// <summary>Ghi một sự kiện vào nhật ký thay đổi dưới tên người đang dùng.</summary>
    public void Record(string action, string detail,
        IReadOnlyDictionary<string, string>? data = null)
    {
        _audit.Write(UserName, Role.ToString(), action, ProductForAudit(), detail, data);
    }
}
