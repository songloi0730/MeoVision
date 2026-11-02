#nullable enable
using System.Drawing;
using System.Windows.Forms;

namespace MeoVision.UI;

public partial class FormTeach
{
    private System.ComponentModel.IContainer? components;
    private PictureBox pictureBoxTrain;
    private Label lblInstructions;
    private Label lblPatternName;
    private TextBox txtPatternName;
    private Button btnTeach;
    private Button btnClose;
    private Label lblStatus;

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        pictureBoxTrain = new PictureBox();
        lblInstructions = new Label();
        lblPatternName = new Label();
        txtPatternName = new TextBox();
        btnTeach = new Button();
        btnClose = new Button();
        lblStatus = new Label();

        ((System.ComponentModel.ISupportInitialize)pictureBoxTrain).BeginInit();
        SuspendLayout();

        // pictureBoxTrain
        pictureBoxTrain.BorderStyle = BorderStyle.FixedSingle;
        pictureBoxTrain.Dock = DockStyle.Fill;
        pictureBoxTrain.SizeMode = PictureBoxSizeMode.Zoom;
        pictureBoxTrain.BackColor = Color.Black;
        pictureBoxTrain.Cursor = Cursors.Cross;

        // lblInstructions
        lblInstructions.Dock = DockStyle.Top;
        lblInstructions.Height = 24;
        lblInstructions.Text = "Kéo chuột trên ảnh để chọn vùng train, đặt tên mẫu, rồi bấm \"Dạy mẫu\".";

        // Panel dưới cùng
        Panel bottomPanel = new()
        {
            Dock = DockStyle.Bottom,
            Height = 90,
            Padding = new Padding(8)
        };

        lblPatternName.Text = "Tên mẫu:";
        lblPatternName.AutoSize = true;
        lblPatternName.Location = new Point(8, 12);

        txtPatternName.Location = new Point(70, 8);
        txtPatternName.Width = 220;

        btnTeach.Text = "Dạy mẫu";
        btnTeach.Location = new Point(300, 6);
        btnTeach.Width = 100;

        btnClose.Text = "Đóng";
        btnClose.Location = new Point(410, 6);
        btnClose.Width = 80;

        lblStatus.AutoSize = false;
        lblStatus.Location = new Point(8, 40);
        lblStatus.Size = new Size(500, 40);
        lblStatus.Text = string.Empty;

        bottomPanel.Controls.Add(lblStatus);
        bottomPanel.Controls.Add(btnClose);
        bottomPanel.Controls.Add(btnTeach);
        bottomPanel.Controls.Add(txtPatternName);
        bottomPanel.Controls.Add(lblPatternName);

        // FormTeach
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(700, 560);
        Controls.Add(pictureBoxTrain);
        Controls.Add(lblInstructions);
        Controls.Add(bottomPanel);
        MinimumSize = new Size(500, 400);
        Text = "MeoVision — Dạy mẫu (Teach)";

        ((System.ComponentModel.ISupportInitialize)pictureBoxTrain).EndInit();
        ResumeLayout(false);
    }
}
