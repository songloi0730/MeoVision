using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using MeoVision.Core;
using MeoVision.Core.Security;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace MeoVision.App.Security;

/// <summary>Một tài khoản đã lưu. Không bao giờ lưu mật khẩu — chỉ salt và mã băm PBKDF2.</summary>
public sealed class UserAccount
{
    public string Name { get; set; } = string.Empty;

    public UserRole Role { get; set; }

    public string Salt { get; set; } = string.Empty;

    public string Hash { get; set; } = string.Empty;

    public int Iterations { get; set; }

    /// <summary>Vẫn là mật khẩu cài sẵn — màn hình nhắc, KHÔNG ép đổi (tài khoản dùng chung).</summary>
    public bool IsDefaultPassword { get; set; }
}

/// <summary>Nội dung <c>users.json</c>.</summary>
public sealed class UsersFile
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public List<UserAccount> Users { get; set; } = new();
}

/// <summary>Kết quả xử lý file khôi phục.</summary>
public enum RecoveryOutcome
{
    /// <summary>Không có file.</summary>
    None,

    /// <summary>Đã đặt lại mật khẩu cho các tài khoản Quản trị.</summary>
    Applied,

    /// <summary>Mật khẩu trong file quá ngắn — file đã xoá, không đổi gì.</summary>
    Invalid,

    /// <summary>File không do quản trị Windows tạo — đã xoá, không đổi gì (Chương 24).</summary>
    Untrusted,
}

/// <summary>Kết quả + tên các tài khoản đã đặt lại (rỗng nếu không đặt lại ai).</summary>
public sealed record RecoveryResult(RecoveryOutcome Outcome, IReadOnlyList<string> Admins,
    string Owner);

/// <summary>
/// Tài khoản của trạm (Chương 23, mục 23.2). Chính sách nhà máy (tác giả chốt): KHÔNG khoá tài
/// khoản khi nhập sai — máy đứng lúc 2 giờ sáng vì kỹ sư gõ nhầm ba lần đắt hơn rủi ro dò mật khẩu
/// tại chỗ; KHÔNG ép đổi mật khẩu lần đầu — tài khoản dùng chung theo vai, người đổi không báo được
/// các ca khác; PHẢI có đường khôi phục quyền Quản trị, và đường đó phải ồn ào.
/// </summary>
public sealed class UserStore
{
    public const string OperatorName = "operator";
    public const string RecoveryFileName = "recover-admin.txt";
    public const int MinPasswordLength = 4;
    private const int Iterations = 100_000;
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    private static readonly JsonSerializerSettings JsonSettings = new()
    {
        Formatting = Formatting.Indented,
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        Converters = { new StringEnumConverter() },
    };

    private readonly string _file;
    private readonly string _recoveryFile;
    private readonly Func<string, string?> _trustedOwner;
    private readonly object _sync = new();
    private UsersFile _users = new();

    /// <param name="recoveryFile">File khôi phục mật khẩu Quản trị.</param>
    /// <param name="trustedOwner">Tên chủ file nếu chủ đó được tin, null nếu không; mặc định
    /// <see cref="AdministratorsOwner"/>. Truyền vào để test được cả hai nhánh.</param>
    public UserStore(string file, string recoveryFile, Func<string, string?>? trustedOwner = null)
    {
        Guard.ThrowIfNull(file, nameof(file));
        Guard.ThrowIfNull(recoveryFile, nameof(recoveryFile));
        _file = file;
        _recoveryFile = recoveryFile;
        _trustedOwner = trustedOwner ?? AdministratorsOwner;
    }

    public string RecoveryFile => _recoveryFile;

    /// <summary>Đọc users.json; lần chạy đầu tạo hai tài khoản cài sẵn engineer / admin.</summary>
    public void Load()
    {
        lock (_sync)
        {
            if (!File.Exists(_file))
            {
                _users = new UsersFile();
                _users.Users.Add(Create("engineer", UserRole.Engineer, "engineer", true));
                _users.Users.Add(Create("admin", UserRole.Administrator, "admin", isDefault: true));
                SaveLocked();
                return;
            }
            _users = JsonConvert.DeserializeObject<UsersFile>(
                         File.ReadAllText(_file, Encoding.UTF8), JsonSettings) ??
                     throw new InvalidDataException("users.json is empty");
        }
    }

    public IReadOnlyList<UserAccount> Accounts
    {
        get { lock (_sync) { return _users.Users.ToList(); } }
    }

    /// <summary>Đúng mật khẩu thì trả vai trò; sai thì null. Không đếm lần sai, không khoá.</summary>
    public UserRole? Verify(string name, string password)
    {
        Guard.ThrowIfNull(name, nameof(name));
        Guard.ThrowIfNull(password, nameof(password));
        UserAccount? account;
        lock (_sync)
        {
            account = Find(name);
        }
        if (account is null)
        {
            return null;
        }
        byte[] expected = Convert.FromBase64String(account.Hash);
        byte[] actual = Derive(password, Convert.FromBase64String(account.Salt),
            account.Iterations);
        return FixedTimeEquals(expected, actual) ? account.Role : null;
    }

    public void SetPassword(string name, string password)
    {
        Guard.ThrowIfNull(name, nameof(name));
        Guard.ThrowIfNull(password, nameof(password));
        if (password.Length < MinPasswordLength)
        {
            throw new ArgumentException(
                $"Password must have at least {MinPasswordLength} characters");
        }
        lock (_sync)
        {
            UserAccount account = Find(name) ?? throw new ArgumentException("Unknown user " + name);
            UserAccount fresh = Create(account.Name, account.Role, password, isDefault: false);
            account.Salt = fresh.Salt;
            account.Hash = fresh.Hash;
            account.Iterations = fresh.Iterations;
            account.IsDefaultPassword = false;
            SaveLocked();
        }
    }

    /// <summary>
    /// Khôi phục quyền Quản trị: file chứa mật khẩu mới (dòng đầu) được đọc, đặt cho mọi tài khoản
    /// Quản trị, rồi XOÁ ngay. Chỉ nhận file do QUẢN TRỊ WINDOWS tạo (Chương 24, mục 24.2.3): thư
    /// mục dữ liệu cho mọi tài khoản thường tạo file mới. Người gọi phải ghi nhật ký thay đổi và
    /// báo trên màn hình — đường tắt có chủ đích thì phải ồn ào.
    /// </summary>
    public RecoveryResult ApplyRecovery()
    {
        if (!File.Exists(_recoveryFile))
        {
            return new RecoveryResult(RecoveryOutcome.None, Array.Empty<string>(), string.Empty);
        }
        string? owner = _trustedOwner(_recoveryFile);
        string ownerName = owner ?? OwnerName(_recoveryFile);   // đọc TRƯỚC khi xoá
        string password = File.ReadAllLines(_recoveryFile, Encoding.UTF8).FirstOrDefault()?.Trim()
                          ?? string.Empty;
        File.Delete(_recoveryFile);   // dùng một lần, kể cả khi bị từ chối
        if (owner is null)
        {
            return new RecoveryResult(RecoveryOutcome.Untrusted, Array.Empty<string>(),
                ownerName);
        }
        if (password.Length < MinPasswordLength)
        {
            return new RecoveryResult(RecoveryOutcome.Invalid, Array.Empty<string>(), owner);
        }
        List<string> names;
        lock (_sync)
        {
            names = _users.Users.Where(u => u.Role == UserRole.Administrator).Select(u => u.Name)
                .ToList();
            if (names.Count == 0)
            {
                _users.Users.Add(Create("admin", UserRole.Administrator, password, false));
                names.Add("admin");
                SaveLocked();
            }
        }
        foreach (string name in names)
        {
            SetPassword(name, password);
        }
        return new RecoveryResult(RecoveryOutcome.Applied, names, owner);
    }

    /// <summary>
    /// Chủ file là nhóm <c>BUILTIN\Administrators</c> hoặc <c>SYSTEM</c>? File tạo từ cửa sổ lệnh
    /// "Run as administrator" có chủ là nhóm Administrators; file do một phiên thường tạo (kể cả
    /// của tài khoản thuộc nhóm Administrators nhưng chưa nâng quyền) có chủ là chính người dùng
    /// đó.
    /// </summary>
    public static string? AdministratorsOwner(string file)
    {
        SecurityIdentifier? sid = File.GetAccessControl(file, AccessControlSections.Owner)
            .GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
        return sid is not null && (sid.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid) ||
                                   sid.IsWellKnown(WellKnownSidType.LocalSystemSid))
            ? OwnerName(file)
            : null;
    }

    private static string OwnerName(string file)
    {
        try
        {
            return File.GetAccessControl(file, AccessControlSections.Owner)
                .GetOwner(typeof(NTAccount))?.Value ?? "?";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException ||
                                   ex is IdentityNotMappedException)
        {
            return "?";
        }
    }

    private UserAccount? Find(string name) =>
        _users.Users.Find(u => string.Equals(u.Name, name, StringComparison.OrdinalIgnoreCase));

    private void SaveLocked()
    {
        string temp = _file + ".tmp";
        File.WriteAllText(temp, JsonConvert.SerializeObject(_users, JsonSettings),
            new UTF8Encoding(false));
        if (File.Exists(_file))
        {
            File.Replace(temp, _file, null);
        }
        else
        {
            File.Move(temp, _file);
        }
    }

    private static UserAccount Create(string name, UserRole role, string password, bool isDefault)
    {
        byte[] salt = new byte[SaltBytes];
        using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(salt);
        }
        return new UserAccount
        {
            Name = name, Role = role, Salt = Convert.ToBase64String(salt),
            Hash = Convert.ToBase64String(Derive(password, salt, Iterations)),
            Iterations = Iterations, IsDefaultPassword = isDefault,
        };
    }

    private static byte[] Derive(string password, byte[] salt, int iterations)
    {
        using Rfc2898DeriveBytes kdf = new(password, salt, iterations, HashAlgorithmName.SHA256);
        return kdf.GetBytes(HashBytes);
    }

    /// <summary>So hết cả chuỗi — thời gian so không lộ đúng được bao nhiêu byte đầu.</summary>
    private static bool FixedTimeEquals(byte[] a, byte[] b)
    {
        int diff = a.Length ^ b.Length;
        for (int i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            diff |= a[i] ^ b[i];
        }
        return diff == 0;
    }
}
