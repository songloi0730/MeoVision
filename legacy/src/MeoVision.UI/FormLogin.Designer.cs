#nullable enable
using System.Drawing;
using System.Windows.Forms;

namespace MeoVision.UI;

public partial class FormLogin
{
    private System.ComponentModel.IContainer? components;
    private Label lblPrompt;
    private TextBox txtPassword;
    private Button btnOk;
    private Button btnCancel;
    private Label lblError;

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        lblPrompt = new Label();
        txtPassword = new TextBox();
        btnOk = new Button();
        btnCancel = new Button();
        lblError = new Label();

        SuspendLayout();

        lblPrompt.AutoSize = true;
        lblPrompt.Location = new Point(16, 18);
        lblPrompt.Text = "Nhập mật khẩu Engineer để mở khoá các công cụ kỹ sư:";

        txtPassword.Location = new Point(16, 44);
        txtPassword.Width = 260;
        txtPassword.UseSystemPasswordChar = true;

        lblError.AutoSize = true;
        lblError.Location = new Point(16, 72);
        lblError.ForeColor = Color.Firebrick;
        lblError.Text = string.Empty;

        btnOk.Text = "Đăng nhập";
        btnOk.Location = new Point(120, 100);
        btnOk.DialogResult = DialogResult.None;

        btnCancel.Text = "Huỷ";
        btnCancel.Location = new Point(201, 100);
        btnCancel.DialogResult = DialogResult.Cancel;

        AcceptButton = btnOk;
        CancelButton = btnCancel;
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(296, 138);
        Controls.Add(lblPrompt);
        Controls.Add(txtPassword);
        Controls.Add(lblError);
        Controls.Add(btnOk);
        Controls.Add(btnCancel);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Text = "Đăng nhập Engineer";

        ResumeLayout(false);
        PerformLayout();
    }
}
