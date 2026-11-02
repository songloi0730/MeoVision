// -------------------------------------------------------
// File:    FormImageRetentionSettings.cs
// Project: MeoVision.UI
// Purpose: Cấu hình chính sách vòng đời ảnh NG (số ngày giữ, ngưỡng dung lượng) — Ch14 §14.5.2.
//          Ghi thẳng xuống appsettings.json (không live-reload — FileSystemNgImageArchiver chỉ
//          đọc giá trị này 1 lần lúc khởi động, xem DESIGN_LOG.md mục [8]).
// -------------------------------------------------------
using System;
using System.IO;
using System.Windows.Forms;
using MeoVision.Application;
using MeoVision.Core;
using Newtonsoft.Json.Linq;

namespace MeoVision.UI;

public partial class FormImageRetentionSettings : Form
{
    private const long BytesPerGigabyte = 1024L * 1024 * 1024;

    private readonly MeoVisionHostOptions _options;
    private readonly string _appSettingsPath;

    public FormImageRetentionSettings(MeoVisionHostOptions options)
    {
        Guard.ThrowIfNull(options, nameof(options));

        InitializeComponent();

        _options = options;
        _appSettingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");

        // net48 không có Math.Clamp (chỉ .NET Core 2.0+/.NET Standard 2.1+) — Min/Max lồng nhau
        // thay thế, không cần helper riêng cho đúng 3 chỗ dùng trong cả project.
        numRetentionDays.Value = Math.Min(Math.Max(_options.NgImageRetentionDays, 1), 3650);
        numMaxGigabytes.Value = Math.Min(
            Math.Max((decimal)_options.NgImageMaxBytes / BytesPerGigabyte, 1), 10000);

        btnSave.Click += BtnSave_Click;
    }

    private void BtnSave_Click(object? sender, EventArgs e)
    {
        int retentionDays = (int)numRetentionDays.Value;
        long maxBytes = (long)(numMaxGigabytes.Value * BytesPerGigabyte);

        try
        {
            SaveToAppSettingsFile(retentionDays, maxBytes);

            // Cập nhật luôn instance đang chạy — không đổi hành vi FileSystemNgImageArchiver hiện
            // tại (đã chụp giá trị cũ vào readonly field lúc khởi tạo), nhưng để code đọc
            // MeoVisionHostOptions sau thời điểm này thấy giá trị mới ngay trong phiên hiện tại.
            _options.NgImageRetentionDays = retentionDays;
            _options.NgImageMaxBytes = maxBytes;

            lblStatus.ForeColor = System.Drawing.Color.ForestGreen;
            lblStatus.Text = "Đã lưu — áp dụng từ lần chạy MeoVision tiếp theo.";
        }
#pragma warning disable CA1031
        catch (Exception ex)
#pragma warning restore CA1031
        {
            lblStatus.ForeColor = System.Drawing.Color.Firebrick;
            lblStatus.Text = $"Lưu thất bại: {ex.Message}";
        }
    }

    // Đọc/ghi nguyên file appsettings.json qua JObject (không qua kiểu MeoVisionHostOptions) để
    // chỉ sửa đúng 2 field cần đổi, giữ nguyên mọi section khác (kể cả section chưa biết tới) —
    // tránh mất cấu hình nếu ai đó thêm field mới vào appsettings.json sau này.
    private void SaveToAppSettingsFile(int retentionDays, long maxBytes)
    {
        string json = File.ReadAllText(_appSettingsPath);
        JObject root = JObject.Parse(json);
        JObject meoVisionSection = (JObject?)root["MeoVision"]
            ?? throw new InvalidOperationException("appsettings.json thiếu section 'MeoVision'.");

        meoVisionSection["NgImageRetentionDays"] = retentionDays;
        meoVisionSection["NgImageMaxBytes"] = maxBytes;

        File.WriteAllText(_appSettingsPath, root.ToString());
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
