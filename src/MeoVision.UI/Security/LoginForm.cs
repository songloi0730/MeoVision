using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using MeoVision.App.Security;
using MeoVision.Core;
using MeoVision.Core.Localization;
using MeoVision.Core.Security;

namespace MeoVision.UI.Security;

/// <summary>
/// Đăng nhập (Chương 23, mục 23.2): chọn tài khoản, gõ mật khẩu. Đã đăng nhập thì đổi được mật khẩu
/// của chính mình, hoặc về Vận hành. Sai mật khẩu chỉ báo sai — không đếm, không khoá.
/// </summary>
public sealed class LoginForm : Form
{
    private readonly ILocalizer _text;
    private readonly Session _session;
    private readonly ComboBox _user = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList, Width = 260,
    };
    private readonly TextBox _password = new() { UseSystemPasswordChar = true, Width = 260 };
    private readonly TextBox _new = new() { UseSystemPasswordChar = true, Width = 260 };
    private readonly TextBox _confirm = new() { UseSystemPasswordChar = true, Width = 260 };
    private readonly Label _message = new() { AutoSize = true, ForeColor = Color.Firebrick };
    private readonly GroupBox _change = new() { Name = "changeGroup", Width = 560, Height = 150 };

    private LoginForm(ILocalizer text, Session session, UserStore users)
    {
        _text = text;
        _session = session;
        Name = "LoginForm";
        Size = new Size(600, 440);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        Font = new Font("Segoe UI", 11f);

        foreach (UserAccount account in users.Accounts.OrderBy(a => a.Role))
        {
            _user.Items.Add(account.Name);
        }
        _user.SelectedItem = _user.Items.Cast<string>()
            .FirstOrDefault(n => n == session.UserName) ??
            _user.Items.Cast<string>().FirstOrDefault();

        TableLayoutPanel grid = new()
        {
            Dock = DockStyle.Top, Height = 170, ColumnCount = 2, Padding = new Padding(12),
        };
        grid.Controls.Add(new Label { Name = "userLabel", AutoSize = true }, 0, 0);
        grid.Controls.Add(_user, 1, 0);
        grid.Controls.Add(new Label { Name = "passwordLabel", AutoSize = true }, 0, 1);
        grid.Controls.Add(_password, 1, 1);
        Button login = new() { Name = "login", Width = 160, Height = 40 };
        Button logout = new() { Name = "logout", Width = 160, Height = 40 };
        FlowLayoutPanel actions = new() { AutoSize = true };
        actions.Controls.Add(login);
        actions.Controls.Add(logout);
        grid.Controls.Add(actions, 1, 2);
        grid.Controls.Add(_message, 1, 3);

        TableLayoutPanel change = new() { Dock = DockStyle.Fill, ColumnCount = 2 };
        change.Controls.Add(new Label { Name = "newLabel", AutoSize = true }, 0, 0);
        change.Controls.Add(_new, 1, 0);
        change.Controls.Add(new Label { Name = "confirmLabel", AutoSize = true }, 0, 1);
        change.Controls.Add(_confirm, 1, 1);
        Button save = new() { Name = "savePassword", Width = 200, Height = 36 };
        change.Controls.Add(save, 1, 2);
        _change.Controls.Add(change);
        _change.Location = new Point(14, 190);

        Controls.Add(_change);
        Controls.Add(grid);
        AcceptButton = login;
        login.Click += (_, _) => OnLogin();
        logout.Click += (_, _) =>
        {
            _session.Logout();
            Close();
        };
        save.Click += (_, _) => OnChangePassword();
        UiText.Apply(this, text);
        RefreshState();
        Shown += (_, _) => _password.Focus();
    }

    public static void ShowDialog(IWin32Window owner, ILocalizer text, Session session,
        UserStore users)
    {
        Guard.ThrowIfNull(text, nameof(text));
        Guard.ThrowIfNull(session, nameof(session));
        Guard.ThrowIfNull(users, nameof(users));
        using LoginForm form = new(text, session, users);
        form.ShowDialog(owner);
    }

    private void OnLogin()
    {
        if (_user.SelectedItem is not string name)
        {
            return;
        }
        if (_session.Login(name, _password.Text))
        {
            Close();
            return;
        }
        _password.Clear();
        _message.Text = _text.Text("LoginForm.wrong");
    }

    private void OnChangePassword()
    {
        if (_new.Text != _confirm.Text)
        {
            _message.Text = _text.Text("LoginForm.mismatch");
            return;
        }
        if (_new.Text.Length < UserStore.MinPasswordLength)
        {
            _message.Text = _text.Text("LoginForm.tooShort", UserStore.MinPasswordLength);
            return;
        }
        _session.ChangePassword(_new.Text);
        _new.Clear();
        _confirm.Clear();
        _message.ForeColor = Color.DarkGreen;
        _message.Text = _text.Text("LoginForm.changed");
    }

    private void RefreshState()
    {
        _change.Enabled = _session.Role != UserRole.Operator;
        _change.Text = _text.Text("LoginForm.changeGroup", _session.UserName);
    }
}
