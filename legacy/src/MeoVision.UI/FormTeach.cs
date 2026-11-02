// -------------------------------------------------------
// File:    FormTeach.cs
// Project: MeoVision.UI
// Purpose: Màn Dạy mẫu (Teach) thu gọn — Ch14 §14.4.2: chọn vùng train trên ảnh vừa chụp, gọi
//          IVisionEngine.TeachPatternAsync. KHÔNG nhúng bộ soạn thảo ToolBlock đầy đủ (đối lập có
//          chủ đích với RefVis-A — xem DESIGN_LOG.md mục [8]).
// -------------------------------------------------------
using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using MeoVision.Core;
using MeoVision.Core.Contracts;

namespace MeoVision.UI;

public partial class FormTeach : Form
{
    private readonly IVisionEngine _engine;
    private readonly Bitmap _sourceImage;
    private Point _dragStartImagePoint;
    private bool _dragging;
    private Rectangle _selectedRegion;

    public FormTeach(IVisionEngine engine, Bitmap sourceImage)
    {
        Guard.ThrowIfNull(engine, nameof(engine));
        Guard.ThrowIfNull(sourceImage, nameof(sourceImage));

        InitializeComponent();

        _engine = engine;
        // Clone — form KHÔNG sở hữu ảnh gốc đang hiển thị ở FormMain (có thể bị Dispose/thay thế
        // bởi chu trình tiếp theo trong khi form Teach vẫn đang mở).
        _sourceImage = (Bitmap)sourceImage.Clone();
        pictureBoxTrain.Image = _sourceImage;

        pictureBoxTrain.MouseDown += PictureBoxTrain_MouseDown;
        pictureBoxTrain.MouseMove += PictureBoxTrain_MouseMove;
        pictureBoxTrain.MouseUp += PictureBoxTrain_MouseUp;
        pictureBoxTrain.Paint += PictureBoxTrain_Paint;
        btnTeach.Click += BtnTeach_Click;
        btnClose.Click += (_, _) => Close();
    }

    // PictureBoxSizeMode.Zoom giữ tỉ lệ khung hình và canh giữa — phải tự tính vùng ảnh thật sự
    // hiển thị bên trong control (có viền đệm 2 bên) để chuyển toạ độ chuột ↔ toạ độ pixel ảnh.
    private RectangleF GetDisplayRectangle()
    {
        Size imgSize = _sourceImage.Size;
        Size ctrlSize = pictureBoxTrain.ClientSize;
        float scale = Math.Min((float)ctrlSize.Width / imgSize.Width, (float)ctrlSize.Height / imgSize.Height);
        float displayWidth = imgSize.Width * scale;
        float displayHeight = imgSize.Height * scale;
        float offsetX = (ctrlSize.Width - displayWidth) / 2f;
        float offsetY = (ctrlSize.Height - displayHeight) / 2f;
        return new RectangleF(offsetX, offsetY, displayWidth, displayHeight);
    }

    private Point ControlToImagePoint(Point controlPoint)
    {
        RectangleF display = GetDisplayRectangle();
        if (display.Width <= 0 || display.Height <= 0)
        {
            return Point.Empty;
        }

        float scale = display.Width / _sourceImage.Width;
        int imageX = (int)((controlPoint.X - display.X) / scale);
        int imageY = (int)((controlPoint.Y - display.Y) / scale);
        // net48 không có Math.Clamp — Min/Max lồng nhau thay thế.
        imageX = Math.Min(Math.Max(imageX, 0), _sourceImage.Width - 1);
        imageY = Math.Min(Math.Max(imageY, 0), _sourceImage.Height - 1);
        return new Point(imageX, imageY);
    }

    private Rectangle ImageToControlRectangle(Rectangle imageRect)
    {
        RectangleF display = GetDisplayRectangle();
        float scale = display.Width / _sourceImage.Width;
        return new Rectangle(
            (int)(display.X + imageRect.X * scale),
            (int)(display.Y + imageRect.Y * scale),
            (int)(imageRect.Width * scale),
            (int)(imageRect.Height * scale));
    }

    private void PictureBoxTrain_MouseDown(object? sender, MouseEventArgs e)
    {
        _dragging = true;
        _dragStartImagePoint = ControlToImagePoint(e.Location);
        _selectedRegion = new Rectangle(_dragStartImagePoint, Size.Empty);
    }

    private void PictureBoxTrain_MouseMove(object? sender, MouseEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        Point current = ControlToImagePoint(e.Location);
        int x = Math.Min(_dragStartImagePoint.X, current.X);
        int y = Math.Min(_dragStartImagePoint.Y, current.Y);
        int width = Math.Abs(current.X - _dragStartImagePoint.X);
        int height = Math.Abs(current.Y - _dragStartImagePoint.Y);
        _selectedRegion = new Rectangle(x, y, width, height);
        pictureBoxTrain.Invalidate();
    }

    private void PictureBoxTrain_MouseUp(object? sender, MouseEventArgs e)
    {
        _dragging = false;
    }

    private void PictureBoxTrain_Paint(object? sender, PaintEventArgs e)
    {
        if (_selectedRegion.Width <= 0 || _selectedRegion.Height <= 0)
        {
            return;
        }

        Rectangle controlRect = ImageToControlRectangle(_selectedRegion);
        using Pen pen = new(Color.Lime, 2);
        e.Graphics.DrawRectangle(pen, controlRect);
    }

    private async void BtnTeach_Click(object? sender, EventArgs e)
    {
        if (_selectedRegion.Width <= 0 || _selectedRegion.Height <= 0)
        {
            lblStatus.ForeColor = Color.Firebrick;
            lblStatus.Text = "Chưa chọn vùng train — kéo chuột trên ảnh trước.";
            return;
        }

        if (string.IsNullOrWhiteSpace(txtPatternName.Text))
        {
            lblStatus.ForeColor = Color.Firebrick;
            lblStatus.Text = "Chưa đặt tên mẫu.";
            return;
        }

        btnTeach.Enabled = false;
        try
        {
            VisionTrainRegion trainRegion = new(
                txtPatternName.Text.Trim(), _selectedRegion, (Bitmap)_sourceImage.Clone());
            await _engine.TeachPatternAsync(trainRegion).ConfigureAwait(true);

            lblStatus.ForeColor = Color.ForestGreen;
            lblStatus.Text = $"Đã dạy mẫu '{trainRegion.PatternName}' thành công " +
                $"({_engine.EngineName}). Bản giả lập chỉ ghi log, không ghi file .vpp thật — " +
                "cần Engine thật + camera + .vpp thật để kiểm tra backup .vpp (Ch14 §14.4.2).";
        }
#pragma warning disable CA1031
        catch (Exception ex)
#pragma warning restore CA1031
        {
            lblStatus.ForeColor = Color.Firebrick;
            lblStatus.Text = $"Dạy mẫu thất bại: {ex.Message}";
        }
        finally
        {
            btnTeach.Enabled = true;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            components?.Dispose();
            _sourceImage.Dispose();
        }

        base.Dispose(disposing);
    }
}
