#nullable enable
using System.Drawing;
using System.Windows.Forms;

namespace MeoVision.UI;

public partial class FormNgImages
{
    private System.ComponentModel.IContainer? components;
    private Label lblFrom;
    private DateTimePicker dtpFrom;
    private Label lblTo;
    private DateTimePicker dtpTo;
    private Button btnFilter;
    private ListBox lstImages;
    private PictureBox pictureBoxPreview;
    private Label lblPath;

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        lblFrom = new Label();
        dtpFrom = new DateTimePicker();
        lblTo = new Label();
        dtpTo = new DateTimePicker();
        btnFilter = new Button();
        lstImages = new ListBox();
        pictureBoxPreview = new PictureBox();
        lblPath = new Label();

        ((System.ComponentModel.ISupportInitialize)pictureBoxPreview).BeginInit();
        SuspendLayout();

        // Panel lọc theo ngày
        Panel filterPanel = new() { Dock = DockStyle.Top, Height = 40, Padding = new Padding(8, 6, 8, 6) };

        lblFrom.Text = "Từ:";
        lblFrom.AutoSize = true;
        lblFrom.Location = new Point(8, 10);

        dtpFrom.Format = DateTimePickerFormat.Short;
        dtpFrom.Location = new Point(36, 6);
        dtpFrom.Width = 110;

        lblTo.Text = "Đến:";
        lblTo.AutoSize = true;
        lblTo.Location = new Point(156, 10);

        dtpTo.Format = DateTimePickerFormat.Short;
        dtpTo.Location = new Point(190, 6);
        dtpTo.Width = 110;

        btnFilter.Text = "Lọc";
        btnFilter.Location = new Point(310, 4);
        btnFilter.Width = 80;

        filterPanel.Controls.Add(lblFrom);
        filterPanel.Controls.Add(dtpFrom);
        filterPanel.Controls.Add(lblTo);
        filterPanel.Controls.Add(dtpTo);
        filterPanel.Controls.Add(btnFilter);

        // lstImages
        lstImages.Dock = DockStyle.Left;
        lstImages.Width = 320;
        lstImages.IntegralHeight = false;

        // pictureBoxPreview
        pictureBoxPreview.Dock = DockStyle.Fill;
        pictureBoxPreview.SizeMode = PictureBoxSizeMode.Zoom;
        pictureBoxPreview.BackColor = Color.Black;
        pictureBoxPreview.BorderStyle = BorderStyle.FixedSingle;

        // lblPath
        lblPath.Dock = DockStyle.Bottom;
        lblPath.Height = 24;
        lblPath.Text = string.Empty;
        lblPath.AutoEllipsis = true;

        Panel previewPanel = new() { Dock = DockStyle.Fill };
        previewPanel.Controls.Add(pictureBoxPreview);
        previewPanel.Controls.Add(lblPath);

        // FormNgImages
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(900, 560);
        Controls.Add(previewPanel);
        Controls.Add(lstImages);
        Controls.Add(filterPanel);
        MinimumSize = new Size(600, 400);
        Text = "MeoVision — Ảnh NG đã lưu";

        ((System.ComponentModel.ISupportInitialize)pictureBoxPreview).EndInit();
        ResumeLayout(false);
    }
}
