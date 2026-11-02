#nullable enable
using System.Drawing;
using System.Windows.Forms;

namespace MeoVision.UI;

public partial class FormImageRetentionSettings
{
    private System.ComponentModel.IContainer? components;
    private Label lblRetentionDays;
    private NumericUpDown numRetentionDays;
    private Label lblMaxGigabytes;
    private NumericUpDown numMaxGigabytes;
    private Label lblNote;
    private Button btnSave;
    private Button btnCancel;
    private Label lblStatus;

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        lblRetentionDays = new Label();
        numRetentionDays = new NumericUpDown();
        lblMaxGigabytes = new Label();
        numMaxGigabytes = new NumericUpDown();
        lblNote = new Label();
        btnSave = new Button();
        btnCancel = new Button();
        lblStatus = new Label();

        ((System.ComponentModel.ISupportInitialize)numRetentionDays).BeginInit();
        ((System.ComponentModel.ISupportInitialize)numMaxGigabytes).BeginInit();
        SuspendLayout();

        lblRetentionDays.Text = "Giữ ảnh NG tối đa (ngày):";
        lblRetentionDays.AutoSize = true;
        lblRetentionDays.Location = new Point(16, 20);

        numRetentionDays.Minimum = 1;
        numRetentionDays.Maximum = 3650;
        numRetentionDays.Location = new Point(200, 16);
        numRetentionDays.Width = 90;

        lblMaxGigabytes.Text = "Ngưỡng dung lượng thư mục (GB):";
        lblMaxGigabytes.AutoSize = true;
        lblMaxGigabytes.Location = new Point(16, 56);

        numMaxGigabytes.DecimalPlaces = 1;
        numMaxGigabytes.Minimum = 1;
        numMaxGigabytes.Maximum = 10000;
        numMaxGigabytes.Location = new Point(200, 52);
        numMaxGigabytes.Width = 90;

        lblNote.AutoSize = false;
        lblNote.Size = new Size(400, 40);
        lblNote.Location = new Point(16, 88);
        lblNote.ForeColor = Color.DimGray;
        lblNote.Text = "Cảnh báo tự động khi dùng quá 90% ngưỡng (Ch14 §14.5.2). " +
            "Áp dụng từ lần chạy MeoVision tiếp theo.";

        btnSave.Text = "Lưu";
        btnSave.Location = new Point(200, 140);
        btnSave.Width = 90;

        btnCancel.Text = "Huỷ";
        btnCancel.Location = new Point(296, 140);
        btnCancel.Width = 90;
        btnCancel.DialogResult = DialogResult.Cancel;

        lblStatus.AutoSize = true;
        lblStatus.Location = new Point(16, 172);
        lblStatus.Text = string.Empty;

        AcceptButton = btnSave;
        CancelButton = btnCancel;
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(410, 200);
        Controls.Add(lblRetentionDays);
        Controls.Add(numRetentionDays);
        Controls.Add(lblMaxGigabytes);
        Controls.Add(numMaxGigabytes);
        Controls.Add(lblNote);
        Controls.Add(btnSave);
        Controls.Add(btnCancel);
        Controls.Add(lblStatus);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Text = "MeoVision — Cấu hình lưu ảnh NG";

        ((System.ComponentModel.ISupportInitialize)numRetentionDays).EndInit();
        ((System.ComponentModel.ISupportInitialize)numMaxGigabytes).EndInit();
        ResumeLayout(false);
    }
}
