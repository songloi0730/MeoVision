// -------------------------------------------------------
// File:    FormLogin.cs
// Project: MeoVision.UI
// Purpose: Dialog đăng nhập tối giản (chỉ 2 cấp Operator/Engineer) gác các công cụ kỹ sư
//          (Dạy mẫu, cấu hình lưu ảnh) — DESIGN_LOG.md mục [8].
// -------------------------------------------------------
using System;
using System.Windows.Forms;
using MeoVision.Application.Auth;
using MeoVision.Core;

namespace MeoVision.UI;

public partial class FormLogin : Form
{
    private readonly ICurrentUserService _currentUserService;

    public FormLogin(ICurrentUserService currentUserService)
    {
        Guard.ThrowIfNull(currentUserService, nameof(currentUserService));

        InitializeComponent();

        _currentUserService = currentUserService;
        btnOk.Click += BtnOk_Click;
    }

    private void BtnOk_Click(object? sender, EventArgs e)
    {
        if (_currentUserService.TryLoginEngineer(txtPassword.Text))
        {
            DialogResult = DialogResult.OK;
            Close();
            return;
        }

        lblError.Text = "Sai mật khẩu.";
        txtPassword.Clear();
        txtPassword.Focus();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            components?.Dispose();
        }

        base.Dispose(disposing);
    }
}
