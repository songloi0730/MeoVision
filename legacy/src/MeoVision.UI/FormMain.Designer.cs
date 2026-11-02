#nullable enable
using System.Drawing;
using System.Windows.Forms;

namespace MeoVision.UI;

public partial class FormMain
{
    // Field ở đây luôn được gán trong InitializeComponent() (gọi từ constructor, file
    // FormMain.cs) — CS8618/CS8602 do phân tích nullable không lần qua được ranh giới 2 file của
    // cùng partial class; tắt ở mức .csproj (xem MeoVision.UI.csproj) thay vì gán "= null!" khắp
    // nơi, đúng cách WinForms+nullable thật xử lý friction point này.
    private System.ComponentModel.IContainer? components;
    private TabControl tabControl;
    private TabPage tabRun;
    private TabPage tabHistory;
    private TabPage tabTrend;
    private TabPage tabConfig;

    // Tab Vận hành
    private PictureBox pictureBoxLive;
    private Label lblStatus;
    private Label lblScore;
    private Button btnStart;
    private Button btnStop;
    private GroupBox grpRecentResults;
    private ListBox lstRecentResults;

    // Tab Lịch sử
    private Button btnRefreshHistory;
    private DataGridView dgvHistory;

    // Tab Xu hướng
    private Panel pnlTrendChart;
    private Label lblTrendStats;

    // Tab Cấu hình
    private Label lblSelectRecipe;
    private ComboBox cmbRecipe;
    private Button btnLoadRecipe;
    private Label lblActiveRecipe;
    private Label lblRecipeDetail;
    private GroupBox grpEngineerTools;
    private Label lblCurrentRole;
    private Button btnLogin;
    private Button btnTeach;
    private Button btnNgImages;
    private Button btnImageSettings;

    // Dải cảnh báo dưới cùng
    private GroupBox grpAlarms;
    private ListBox lstAlarms;

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        tabControl = new TabControl();
        tabRun = new TabPage();
        tabHistory = new TabPage();
        tabTrend = new TabPage();
        tabConfig = new TabPage();
        pictureBoxLive = new PictureBox();
        lblStatus = new Label();
        lblScore = new Label();
        btnStart = new Button();
        btnStop = new Button();
        grpRecentResults = new GroupBox();
        lstRecentResults = new ListBox();
        btnRefreshHistory = new Button();
        dgvHistory = new DataGridView();
        pnlTrendChart = new Panel();
        lblTrendStats = new Label();
        lblSelectRecipe = new Label();
        cmbRecipe = new ComboBox();
        btnLoadRecipe = new Button();
        lblActiveRecipe = new Label();
        lblRecipeDetail = new Label();
        grpEngineerTools = new GroupBox();
        lblCurrentRole = new Label();
        btnLogin = new Button();
        btnTeach = new Button();
        btnNgImages = new Button();
        btnImageSettings = new Button();
        grpAlarms = new GroupBox();
        lstAlarms = new ListBox();

        ((System.ComponentModel.ISupportInitialize)pictureBoxLive).BeginInit();
        ((System.ComponentModel.ISupportInitialize)dgvHistory).BeginInit();
        tabControl.SuspendLayout();
        tabRun.SuspendLayout();
        tabHistory.SuspendLayout();
        tabTrend.SuspendLayout();
        tabConfig.SuspendLayout();
        grpRecentResults.SuspendLayout();
        grpEngineerTools.SuspendLayout();
        grpAlarms.SuspendLayout();
        SuspendLayout();

        // pictureBoxLive
        pictureBoxLive.BorderStyle = BorderStyle.FixedSingle;
        pictureBoxLive.Dock = DockStyle.Fill;
        pictureBoxLive.SizeMode = PictureBoxSizeMode.Zoom;
        pictureBoxLive.BackColor = Color.Black;

        // lblStatus
        lblStatus.Dock = DockStyle.Top;
        lblStatus.Font = new Font("Segoe UI", 18F, FontStyle.Bold);
        lblStatus.Height = 60;
        lblStatus.TextAlign = ContentAlignment.MiddleCenter;
        lblStatus.Text = "IDLE";
        lblStatus.BackColor = Color.Gray;
        lblStatus.ForeColor = Color.White;

        // lblScore
        lblScore.Dock = DockStyle.Top;
        lblScore.Font = new Font("Segoe UI", 11F);
        lblScore.Height = 30;
        lblScore.TextAlign = ContentAlignment.MiddleCenter;
        lblScore.Text = "Score: -";

        // btnStart / btnStop
        btnStart.Text = "Bắt đầu";
        btnStart.Dock = DockStyle.Top;
        btnStart.Height = 36;
        btnStop.Text = "Dừng";
        btnStop.Dock = DockStyle.Top;
        btnStop.Height = 36;
        btnStop.Enabled = false;

        // grpRecentResults
        lstRecentResults.Dock = DockStyle.Fill;
        lstRecentResults.IntegralHeight = false;
        grpRecentResults.Text = "Kết quả gần đây";
        grpRecentResults.Dock = DockStyle.Fill;
        grpRecentResults.Controls.Add(lstRecentResults);

        // Panel bên phải tab Vận hành
        Panel runSidePanel = new()
        {
            Dock = DockStyle.Right,
            Width = 260,
            Padding = new Padding(8)
        };
        runSidePanel.Controls.Add(grpRecentResults);
        runSidePanel.Controls.Add(btnStop);
        runSidePanel.Controls.Add(btnStart);
        runSidePanel.Controls.Add(lblScore);
        runSidePanel.Controls.Add(lblStatus);

        // tabRun
        tabRun.Text = "Vận hành";
        tabRun.Padding = new Padding(6);
        tabRun.Controls.Add(pictureBoxLive);
        tabRun.Controls.Add(runSidePanel);

        // btnRefreshHistory
        btnRefreshHistory.Text = "Làm mới";
        btnRefreshHistory.Dock = DockStyle.Top;
        btnRefreshHistory.Height = 32;

        // dgvHistory
        dgvHistory.Dock = DockStyle.Fill;
        dgvHistory.ReadOnly = true;
        dgvHistory.AllowUserToAddRows = false;
        dgvHistory.AllowUserToDeleteRows = false;
        dgvHistory.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        dgvHistory.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        dgvHistory.Columns.Add("ColTime", "Thời gian");
        dgvHistory.Columns.Add("ColJob", "Job");
        dgvHistory.Columns.Add("ColOutcome", "Kết quả");
        dgvHistory.Columns.Add("ColReason", "Lý do lỗi");
        dgvHistory.Columns.Add("ColScore", "Score");
        dgvHistory.Columns.Add("ColAttempt", "Số lần thử");
        dgvHistory.Columns.Add("ColImage", "Ảnh lưu");

        // tabHistory
        tabHistory.Text = "Lịch sử";
        tabHistory.Padding = new Padding(6);
        tabHistory.Controls.Add(dgvHistory);
        tabHistory.Controls.Add(btnRefreshHistory);

        // pnlTrendChart — vẽ tay bằng GDI+ (Paint event ở FormMain.cs), không cần thư viện chart
        // ngoài (Ch16 §16.4.2: "không cần control chart/Cpk phức tạp").
        pnlTrendChart.Dock = DockStyle.Fill;
        pnlTrendChart.BackColor = Color.White;
        pnlTrendChart.BorderStyle = BorderStyle.FixedSingle;

        lblTrendStats.Dock = DockStyle.Top;
        lblTrendStats.Height = 30;
        lblTrendStats.Font = new Font("Segoe UI", 10F);
        lblTrendStats.Text = "Chưa đủ dữ liệu.";

        // tabTrend
        tabTrend.Text = "Xu hướng";
        tabTrend.Padding = new Padding(6);
        tabTrend.Controls.Add(pnlTrendChart);
        tabTrend.Controls.Add(lblTrendStats);

        // Tab Cấu hình
        lblSelectRecipe.Text = "Recipe:";
        lblSelectRecipe.AutoSize = true;
        lblSelectRecipe.Location = new Point(12, 18);

        cmbRecipe.DropDownStyle = ComboBoxStyle.DropDownList;
        cmbRecipe.Location = new Point(70, 14);
        cmbRecipe.Width = 200;

        btnLoadRecipe.Text = "Nạp recipe";
        btnLoadRecipe.Location = new Point(280, 13);

        lblActiveRecipe.AutoSize = true;
        lblActiveRecipe.Location = new Point(12, 54);
        lblActiveRecipe.Text = "Recipe hiện hành: (chưa nạp)";
        lblActiveRecipe.Font = new Font("Segoe UI", 10F, FontStyle.Bold);

        lblRecipeDetail.AutoSize = true;
        lblRecipeDetail.Location = new Point(12, 82);
        lblRecipeDetail.Text = string.Empty;

        // grpEngineerTools — Dạy mẫu/cấu hình lưu ảnh chỉ mở khi đã đăng nhập Engineer (Ch14
        // §14.4.2) — xem UpdateEngineerToolsEnabled() ở FormMain.cs. Xem ảnh NG mở cho mọi vai trò.
        lblCurrentRole.AutoSize = true;
        lblCurrentRole.Location = new Point(16, 28);
        lblCurrentRole.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
        lblCurrentRole.Text = "Vai trò hiện tại: Operator";

        btnLogin.Text = "Đăng nhập Engineer...";
        btnLogin.Location = new Point(16, 54);
        btnLogin.Width = 180;

        btnTeach.Text = "Dạy mẫu (Teach)...";
        btnTeach.Location = new Point(16, 92);
        btnTeach.Width = 180;
        btnTeach.Enabled = false;

        btnNgImages.Text = "Ảnh NG đã lưu...";
        btnNgImages.Location = new Point(206, 92);
        btnNgImages.Width = 160;

        btnImageSettings.Text = "Cấu hình lưu ảnh...";
        btnImageSettings.Location = new Point(376, 92);
        btnImageSettings.Width = 160;
        btnImageSettings.Enabled = false;

        grpEngineerTools.Text = "Công cụ kỹ sư";
        grpEngineerTools.Location = new Point(12, 120);
        grpEngineerTools.Size = new Size(560, 140);
        grpEngineerTools.Controls.Add(lblCurrentRole);
        grpEngineerTools.Controls.Add(btnLogin);
        grpEngineerTools.Controls.Add(btnTeach);
        grpEngineerTools.Controls.Add(btnNgImages);
        grpEngineerTools.Controls.Add(btnImageSettings);

        tabConfig.Text = "Cấu hình";
        tabConfig.Controls.Add(lblSelectRecipe);
        tabConfig.Controls.Add(cmbRecipe);
        tabConfig.Controls.Add(btnLoadRecipe);
        tabConfig.Controls.Add(lblActiveRecipe);
        tabConfig.Controls.Add(lblRecipeDetail);
        tabConfig.Controls.Add(grpEngineerTools);

        // tabControl
        tabControl.Dock = DockStyle.Fill;
        tabControl.TabPages.Add(tabRun);
        tabControl.TabPages.Add(tabHistory);
        tabControl.TabPages.Add(tabTrend);
        tabControl.TabPages.Add(tabConfig);

        // grpAlarms
        lstAlarms.Dock = DockStyle.Fill;
        lstAlarms.IntegralHeight = false;
        grpAlarms.Text = "Cảnh báo";
        grpAlarms.Dock = DockStyle.Bottom;
        grpAlarms.Height = 110;
        grpAlarms.Controls.Add(lstAlarms);

        // FormMain
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(1000, 650);
        Controls.Add(tabControl);
        Controls.Add(grpAlarms);
        MinimumSize = new Size(800, 500);
        Text = "MeoVision — Trạm kiểm tra";

        ((System.ComponentModel.ISupportInitialize)pictureBoxLive).EndInit();
        ((System.ComponentModel.ISupportInitialize)dgvHistory).EndInit();
        tabRun.ResumeLayout(false);
        tabHistory.ResumeLayout(false);
        tabTrend.ResumeLayout(false);
        tabConfig.ResumeLayout(false);
        tabConfig.PerformLayout();
        grpRecentResults.ResumeLayout(false);
        grpEngineerTools.ResumeLayout(false);
        grpEngineerTools.PerformLayout();
        grpAlarms.ResumeLayout(false);
        tabControl.ResumeLayout(false);
        ResumeLayout(false);
    }
}
