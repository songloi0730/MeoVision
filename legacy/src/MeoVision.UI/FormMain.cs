using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using MeoVision.Application;
using MeoVision.Application.Alarms;
using MeoVision.Application.Auth;
using MeoVision.Application.Hosting;
using MeoVision.Application.Recipes;
using MeoVision.Application.Results;
using MeoVision.Core;
using MeoVision.Core.Contracts;
using Microsoft.Extensions.Logging;

namespace MeoVision.UI;

public partial class FormMain : Form
{
    private const int TrendWindowSize = 30;

    private readonly InspectionCycleService _cycleService;
    private readonly IVisionEngine _engine;
    private readonly IInspectionResultStore _resultStore;
    private readonly IAlarmService _alarmService;
    private readonly IRecipeRepository _recipeRepository;
    private readonly ICurrentUserService _currentUserService;
    private readonly MeoVisionHostOptions _options;
    private readonly ILogger<FormMain> _logger;
    private readonly Timer _runTimer;
    private readonly List<InspectionLogEntry> _trendEntries = new();
    private bool _cycleInFlight;
    private bool _productionBlocked;   // Chương 17: báo cáo khởi động có bước then chốt hỏng

    public FormMain(
        InspectionCycleService cycleService,
        IVisionEngine engine,
        IInspectionResultStore resultStore,
        IAlarmService alarmService,
        IRecipeRepository recipeRepository,
        ICurrentUserService currentUserService,
        MeoVisionHostOptions options,
        ILogger<FormMain> logger)
    {
        Guard.ThrowIfNull(cycleService, nameof(cycleService));
        Guard.ThrowIfNull(engine, nameof(engine));
        Guard.ThrowIfNull(resultStore, nameof(resultStore));
        Guard.ThrowIfNull(alarmService, nameof(alarmService));
        Guard.ThrowIfNull(recipeRepository, nameof(recipeRepository));
        Guard.ThrowIfNull(currentUserService, nameof(currentUserService));
        Guard.ThrowIfNull(options, nameof(options));
        Guard.ThrowIfNull(logger, nameof(logger));

        InitializeComponent();

        _cycleService = cycleService;
        _engine = engine;
        _resultStore = resultStore;
        _alarmService = alarmService;
        _recipeRepository = recipeRepository;
        _currentUserService = currentUserService;
        _options = options;
        _logger = logger;

        _runTimer = new Timer { Interval = 800 };
        _runTimer.Tick += RunTimer_Tick;

        _alarmService.AlarmRaised += OnAlarmRaised;
        _currentUserService.LevelChanged += OnCurrentUserLevelChanged;
        Load += FormMain_Load;
        FormClosing += FormMain_FormClosing;
        btnStart.Click += BtnStart_Click;
        btnStop.Click += BtnStop_Click;
        btnLoadRecipe.Click += BtnLoadRecipe_Click;
        btnRefreshHistory.Click += BtnRefreshHistory_Click;
        btnLogin.Click += BtnLogin_Click;
        btnTeach.Click += BtnTeach_Click;
        btnNgImages.Click += BtnNgImages_Click;
        btnImageSettings.Click += BtnImageSettings_Click;
        pnlTrendChart.Paint += PnlTrendChart_Paint;

        UpdateEngineerToolsUi();
    }

    /// <summary>
    /// Nhận báo cáo khởi động (Chương 17, mục 17.4): hiện mọi bước không OK lên danh sách cảnh
    /// báo; nếu có bước then chốt hỏng thì khoá chạy tự động — vẫn mở được để kỹ sư xem và sửa.
    /// </summary>
    public void ApplyStartupReport(StartupReport report)
    {
        Guard.ThrowIfNull(report, nameof(report));
        foreach (StepResult step in report.Steps.Where(s => s.Status != StepStatus.Ok))
        {
            lstAlarms.Items.Add($"[Khởi động] {step.Name}: {step.Status} — {step.Message}");
        }

        _productionBlocked = !report.CanRunProduction;
        if (_productionBlocked)
        {
            btnStart.Enabled = false;
            Text += " — KHÔNG ĐƯỢC CHẠY TỰ ĐỘNG (xem cảnh báo khởi động)";
        }
    }

    private async void FormMain_Load(object? sender, EventArgs e)
    {
        try
        {
            IReadOnlyList<VisionRecipe> recipes = await _recipeRepository.GetAllAsync();
            cmbRecipe.Items.Clear();
            foreach (VisionRecipe recipe in recipes)
            {
                cmbRecipe.Items.Add(recipe.RecipeId);
            }

            if (cmbRecipe.Items.Contains(_options.ActiveRecipeId))
            {
                cmbRecipe.SelectedItem = _options.ActiveRecipeId;
            }
            else if (cmbRecipe.Items.Count > 0)
            {
                cmbRecipe.SelectedIndex = 0;
            }

            if (cmbRecipe.SelectedItem is string recipeId)
            {
                await LoadRecipeAsync(recipeId);
            }
        }
#pragma warning disable CA1031
        catch (Exception ex)
#pragma warning restore CA1031
        {
            ShowError(ex);
        }
    }

    private async void BtnLoadRecipe_Click(object? sender, EventArgs e)
    {
        if (cmbRecipe.SelectedItem is string recipeId)
        {
            await LoadRecipeAsync(recipeId);
        }
    }

    private async System.Threading.Tasks.Task LoadRecipeAsync(string recipeId)
    {
        try
        {
            await _cycleService.LoadActiveRecipeAsync(recipeId);
            VisionRecipe recipe = await _recipeRepository.GetAsync(recipeId);
            lblActiveRecipe.Text = $"Recipe hiện hành: {recipe.RecipeId} (job={recipe.JobName})";
            string strobeInfo = recipe.StrobeEnabled
                ? $"Strobe ON (delay={recipe.StrobeDelayUs:F0}µs, pulse={recipe.StrobePulseDurationUs:F0}µs)"
                : "Strobe OFF";
            lblRecipeDetail.Text =
                $"Width: {recipe.PartWidthMinMm:F1}-{recipe.PartWidthMaxMm:F1}mm | " +
                $"Số pad: {recipe.ExpectedPadCount} | MinScore: {recipe.MinAcceptScore:F2} | " +
                $"Max retry: {recipe.MaxRetries} | TriggerMode: {recipe.TriggerMode} | {strobeInfo}";
        }
#pragma warning disable CA1031
        catch (Exception ex)
#pragma warning restore CA1031
        {
            ShowError(ex);
        }
    }

    private void BtnStart_Click(object? sender, EventArgs e)
    {
        if (_productionBlocked)
        {
            return;
        }

        btnStart.Enabled = false;
        btnStop.Enabled = true;
        _runTimer.Start();
    }

    private void BtnStop_Click(object? sender, EventArgs e)
    {
        _runTimer.Stop();
        btnStart.Enabled = !_productionBlocked;
        btnStop.Enabled = false;
    }

    private async void RunTimer_Tick(object? sender, EventArgs e)
    {
        if (_cycleInFlight)
        {
            return;
        }

        _cycleInFlight = true;
        try
        {
            VisionResult result = await _cycleService.RunCycleAsync();
            UpdateResultDisplay(result);
            await UpdateTrendDisplayAsync().ConfigureAwait(true);
        }
#pragma warning disable CA1031
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _runTimer.Stop();
            btnStart.Enabled = true;
            btnStop.Enabled = false;
            ShowError(ex);
        }
        finally
        {
            _cycleInFlight = false;
        }
    }

    private void UpdateResultDisplay(VisionResult result)
    {
        Bitmap? previous = pictureBoxLive.Image as Bitmap;
        pictureBoxLive.Image = result.Image;
        // Ảnh cũ chỉ Dispose SAU KHI PictureBox đã nhận ảnh mới — tránh khoảng trống hiển thị và
        // đúng quy tắc sở hữu ghi trên VisionResult.Image (MeoVision.Core).
        previous?.Dispose();

        lblStatus.Text = result.Outcome.ToString().ToUpperInvariant();
        lblStatus.BackColor = result.Outcome switch
        {
            VisionOutcome.Accept => Color.ForestGreen,
            VisionOutcome.Warning => Color.Goldenrod,
            VisionOutcome.Reject or VisionOutcome.Error => Color.Firebrick,
            _ => Color.Gray
        };
        lblScore.Text = $"Score: {result.Score:F3}  (X={result.X:F1} Y={result.Y:F1} A={result.AngleDeg:F1}°)";

        lstRecentResults.Items.Insert(0,
            $"{result.TimestampUtc.ToLocalTime():HH:mm:ss} {result.Outcome} score={result.Score:F3}");
        while (lstRecentResults.Items.Count > 30)
        {
            lstRecentResults.Items.RemoveAt(lstRecentResults.Items.Count - 1);
        }
    }

    // Rolling window N chu trình gần nhất (Ch16 §16.4) — đọc lại từ IInspectionResultStore thay vì
    // tự cộng dồn trong FormMain, để nhất quán với tab Lịch sử và không mất dữ liệu nếu người dùng
    // vừa mở app (chưa Start) rồi chuyển sang tab Xu hướng.
    private async System.Threading.Tasks.Task UpdateTrendDisplayAsync()
    {
        IReadOnlyList<InspectionLogEntry> recent = await _resultStore
            .GetRecentAsync(TrendWindowSize).ConfigureAwait(true);

        _trendEntries.Clear();
        _trendEntries.AddRange(recent);

        lblTrendStats.Text = _trendEntries.Count == 0
            ? "Chưa đủ dữ liệu."
            : $"Trung bình {_trendEntries.Count} chu trình gần nhất: " +
              $"Score={_trendEntries.Average(x => x.Score):F3} | " +
              $"Thời gian xử lý={_trendEntries.Average(x => x.ProcessingTimeMs):F0}ms";

        pnlTrendChart.Invalidate();
    }

    private void PnlTrendChart_Paint(object? sender, PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        if (_trendEntries.Count < 2)
        {
            return;
        }

        Rectangle plotArea = Rectangle.Inflate(pnlTrendChart.ClientRectangle, -36, -16);
        if (plotArea.Width <= 0 || plotArea.Height <= 0)
        {
            return;
        }

        // _trendEntries từ store là mới nhất→cũ nhất (AddFirst) — đảo lại để vẽ trái(cũ)→phải(mới).
        List<InspectionLogEntry> ordered = Enumerable.Reverse(_trendEntries).ToList();

        float ToX(int index) => plotArea.Left + (float)index / (ordered.Count - 1) * plotArea.Width;
        // net48 không có Math.Clamp — Min/Max lồng nhau thay thế.
        float ToY(double score) => plotArea.Bottom - (float)Math.Min(Math.Max(score, 0.0), 1.0) * plotArea.Height;

        using Pen axisPen = new(Color.Black);
        g.DrawLine(axisPen, plotArea.Left, plotArea.Top, plotArea.Left, plotArea.Bottom);
        g.DrawLine(axisPen, plotArea.Left, plotArea.Bottom, plotArea.Right, plotArea.Bottom);
        g.DrawString("1.0", Font, Brushes.Gray, plotArea.Left - 32, plotArea.Top - 6);
        g.DrawString("0.0", Font, Brushes.Gray, plotArea.Left - 32, plotArea.Bottom - 6);

        double? minAcceptScore = _cycleService.ActiveRecipe?.MinAcceptScore;
        if (minAcceptScore is double threshold)
        {
            using Pen thresholdPen = new(Color.Orange) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
            float y = ToY(threshold);
            g.DrawLine(thresholdPen, plotArea.Left, y, plotArea.Right, y);
            g.DrawString($"Ngưỡng {threshold:F2}", Font, Brushes.DarkOrange, plotArea.Right - 90, y - 16);
        }

        using Pen linePen = new(Color.SteelBlue, 2);
        PointF? previousPoint = null;
        for (int i = 0; i < ordered.Count; i++)
        {
            PointF point = new(ToX(i), ToY(ordered[i].Score));
            if (previousPoint is PointF prev)
            {
                g.DrawLine(linePen, prev, point);
            }

            Color dotColor = ordered[i].Outcome switch
            {
                VisionOutcome.Accept => Color.ForestGreen,
                VisionOutcome.Warning => Color.Goldenrod,
                _ => Color.Firebrick
            };
            using Brush dotBrush = new SolidBrush(dotColor);
            g.FillEllipse(dotBrush, point.X - 3, point.Y - 3, 6, 6);
            previousPoint = point;
        }
    }

    private void UpdateEngineerToolsUi()
    {
        bool isEngineer = _currentUserService.Level == UserLevel.Engineer;
        lblCurrentRole.Text = $"Vai trò hiện tại: {(isEngineer ? "Engineer" : "Operator")}";
        btnLogin.Text = isEngineer ? "Đăng xuất" : "Đăng nhập Engineer...";
        btnTeach.Enabled = isEngineer;
        btnImageSettings.Enabled = isEngineer;
    }

    private void OnCurrentUserLevelChanged(object? sender, EventArgs e)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => OnCurrentUserLevelChanged(sender, e)));
            return;
        }

        UpdateEngineerToolsUi();
    }

    private void BtnLogin_Click(object? sender, EventArgs e)
    {
        if (_currentUserService.Level == UserLevel.Engineer)
        {
            _currentUserService.LogoutToOperator();
            return;
        }

        using FormLogin formLogin = new(_currentUserService);
        formLogin.ShowDialog(this);
    }

    private void BtnTeach_Click(object? sender, EventArgs e)
    {
        if (pictureBoxLive.Image is not Bitmap sourceImage)
        {
            MessageBox.Show(
                "Chưa có ảnh nào — chạy ít nhất 1 chu trình ở tab Vận hành trước khi dạy mẫu.",
                "MeoVision", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using FormTeach formTeach = new(_engine, sourceImage);
        formTeach.ShowDialog(this);
    }

    private void BtnNgImages_Click(object? sender, EventArgs e)
    {
        using FormNgImages formNgImages = new(_resultStore);
        formNgImages.ShowDialog(this);
    }

    private void BtnImageSettings_Click(object? sender, EventArgs e)
    {
        using FormImageRetentionSettings formSettings = new(_options);
        formSettings.ShowDialog(this);
    }

    private async void BtnRefreshHistory_Click(object? sender, EventArgs e)
    {
        try
        {
            IReadOnlyList<InspectionLogEntry> entries = await _resultStore.GetRecentAsync(200);
            dgvHistory.Rows.Clear();
            foreach (InspectionLogEntry entry in entries)
            {
                dgvHistory.Rows.Add(
                    entry.TimestampUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
                    entry.JobName,
                    entry.Outcome,
                    entry.FailureReason,
                    entry.Score.ToString("F3"),
                    entry.AttemptCount,
                    entry.SavedImagePath ?? string.Empty);
            }
        }
#pragma warning disable CA1031
        catch (Exception ex)
#pragma warning restore CA1031
        {
            ShowError(ex);
        }
    }

    private void OnAlarmRaised(object? sender, AlarmRaisedEventArgs e)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => OnAlarmRaised(sender, e)));
            return;
        }

        lstAlarms.Items.Insert(0, $"{e.TimestampUtc.ToLocalTime():HH:mm:ss} [{e.Severity}] {e.Code}: {e.Message}");
        while (lstAlarms.Items.Count > 50)
        {
            lstAlarms.Items.RemoveAt(lstAlarms.Items.Count - 1);
        }
    }

    private void FormMain_FormClosing(object? sender, FormClosingEventArgs e)
    {
        _runTimer.Stop();
        _alarmService.AlarmRaised -= OnAlarmRaised;
        _currentUserService.LevelChanged -= OnCurrentUserLevelChanged;
        (pictureBoxLive.Image as Bitmap)?.Dispose();
    }

    private static void ShowError(Exception ex)
    {
        MessageBox.Show(ex.Message, "MeoVision — Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _runTimer.Dispose();
            components?.Dispose();
        }

        base.Dispose(disposing);
    }
}
