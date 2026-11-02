// Spike (bỏ đi sau khi xong): nhúng control VisionPro THẬT vào bố cục mockup MeoVision v2 để kiểm tra
// kích thước, hành vi, cách hiển thị record. Mỗi lần chạy một cảnh, tự chụp cửa sổ (PrintWindow) rồi thoát.
//   UiSpike.exe <cảnh> <thư mục ảnh> [<file .vpp>]
//   cảnh: main | tbedit | qbedit | calib | acq
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Cognex.VisionPro;
using Cognex.VisionPro.QuickBuild;
using Cognex.VisionPro.ToolBlock;
using Cognex.VisionPro.ToolGroup;
using Cognex.VisionPro.CalibFix;
using Cognex.VisionPro.ImageFile;

static class Program
{
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint f);

    static string Scene, OutDir, Vpp, ImagePath;
    static readonly List<string> Log = new List<string>();

    [STAThread]
    static int Main(string[] a)
    {
        SetProcessDPIAware();
        Application.EnableVisualStyles();
        Scene = a.Length > 0 ? a[0] : "main";
        OutDir = a.Length > 1 ? a[1] : ".";
        Vpp = a.Length > 2 ? a[2] : null;
        ImagePath = a.Length > 3 ? a[3] : null;
        Directory.CreateDirectory(OutDir);
        Form f;
        try { f = Build(); }
        catch (Exception ex) { File.WriteAllText(Path.Combine(OutDir, Scene + "_error.txt"), ex.ToString()); return 2; }
        var t = new Timer { Interval = Scene == "main" ? 9000 : 6000 };
        t.Tick += (s, e) =>
        {
            t.Stop();
            Snap(f, Scene);
            foreach (var extra in Extras) { extra(); }
            File.WriteAllLines(Path.Combine(OutDir, Scene + "_log.txt"), Log);
            Environment.Exit(0);
        };
        f.Shown += (s, e) => t.Start();
        Application.Run(f);
        return 0;
    }

    static readonly List<Action> Extras = new List<Action>();

    static void Snap(Control c, string name)
    {
        var r = c.Bounds;
        using (var b = new Bitmap(c.Width, c.Height))
        {
            using (var g = Graphics.FromImage(b))
            {
                IntPtr dc = g.GetHdc();
                PrintWindow(c.Handle, dc, 2);
                g.ReleaseHdc(dc);
            }
            b.Save(Path.Combine(OutDir, name + ".png"));
        }
        Log.Add("snap " + name + " " + c.Width + "x" + c.Height);
    }

    // ---------- khung cửa sổ ----------
    static Form NewForm(string title, int w, int h)
    {
        var f = new Form
        {
            Text = title, StartPosition = FormStartPosition.Manual, Font = new Font("Segoe UI", 9.75f),
            BackColor = Color.FromArgb(240, 240, 240)
        };
        var wa = Screen.PrimaryScreen.WorkingArea;
        f.Bounds = new Rectangle(wa.X, wa.Y, Math.Min(w, wa.Width), Math.Min(h, wa.Height));
        Log.Add("screen " + Screen.PrimaryScreen.Bounds + " work " + wa + " form " + f.Bounds);
        return f;
    }

    static Form Build()
    {
        switch (Scene)
        {
            case "main": case "main2": return BuildMain();
            case "tbedit": case "tbedit2": case "tbedit3": return BuildToolBlockEdit();
            case "main3": return BuildMain();
            case "qbedit": return BuildQuickBuildEdit();
            case "calib": return BuildCalib();
            case "acq": return BuildAcq();
        }
        throw new ArgumentException("cảnh lạ: " + Scene);
    }

    // ---------- màn chính: 2×2 khung, mỗi khung = tiêu đề + CogRecordsDisplay (có combobox record) ----------
    static Form BuildMain()
    {
        var f = NewForm("MeoVision spike — main", 1920, 1040);
        var tool = new ToolStrip { ImageScalingSize = new Size(32, 32), AutoSize = false, Height = 84, GripStyle = ToolStripGripStyle.Hidden };
        foreach (var s in new[] { "Trang chính", "Camera", "Hiệu chuẩn", "Lệnh & job", "Giao tiếp", "Sản phẩm", "Lịch sử", "Cài đặt", "Đăng nhập", "Thoát" })
            tool.Items.Add(new ToolStripButton(s) { AutoSize = false, Size = new Size(84, 76), TextImageRelation = TextImageRelation.ImageAboveText });
        var status = new StatusStrip();
        status.Items.Add("Sản phẩm: P-1024 | Người dùng: Vận hành | TopCam | BottomCam | Server 127.0.0.1:10001");
        bool clean = Scene == "main2" || Scene == "main3";   // bản đã chỉnh: dock hẹp, bỏ combobox record, bỏ thanh cuộn, nền xám
        var dock = new TabControl { Dock = DockStyle.Right, Width = clean ? 360 : 470 };
        dock.TabPages.Add("Nhật ký"); dock.TabPages.Add("Chạy tay"); dock.TabPages.Add("Bộ đếm");
        var log = new ListBox { Dock = DockStyle.Fill, Font = new Font("Consolas", 9.5f) };
        dock.TabPages[0].Controls.Add(log);
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Padding = new Padding(4) };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50)); grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        var tiles = new List<CogRecordsDisplay>();
        string[] names = { "Đọc mã DataMatrix · T1 · TopCam", "PatFlex · T2 · BottomCam", "T1 — ảnh gốc", "T2 — record khác" };
        for (int i = 0; i < 4; i++)
        {
            var p = new Panel { Dock = DockStyle.Fill, Margin = new Padding(3), BackColor = Color.FromArgb(30, 37, 43) };
            var head = new Label { Dock = DockStyle.Top, Height = 34, Text = "  " + names[i], ForeColor = Color.White, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI Semibold", 10f) };
            var rd = new CogRecordsDisplay { Dock = DockStyle.Fill };
            if (clean)
            {
                rd.ShowRecordsDropDown = false;
                rd.Display.HorizontalScrollBar = false;
                rd.Display.VerticalScrollBar = false;
                rd.Display.BackColor = Color.FromArgb(14, 19, 23);
            }
            p.Controls.Add(rd); p.Controls.Add(head);
            grid.Controls.Add(p, i % 2, i / 2);
            tiles.Add(rd);
        }
        f.Controls.Add(grid); f.Controls.Add(dock); f.Controls.Add(tool); f.Controls.Add(status);

        string vpp = Vpp ?? @"C:\Program Files\Cognex\VisionPro\samples\ApplicationWizard\QuickBuild2Jobs\QuickBuild2Jobs.vpp";
        CogJobManager jm = null;
        f.Shown += (s, e) =>
        {
            if (Scene == "main3")   // thử: đặt màu nền display SAU khi control ActiveX đã tạo handle
                foreach (var d in tiles) d.Display.BackColor = Color.FromArgb(14, 19, 23);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            jm = (CogJobManager)CogSerializer.LoadObjectFromFile(vpp);
            Log.Add("load jm " + sw.ElapsedMilliseconds + " ms, jobs=" + jm.JobCount);
            log.Items.Add("Nạp " + Path.GetFileName(vpp) + ": " + jm.JobCount + " job, " + sw.ElapsedMilliseconds + " ms");
            jm.UserQueueResultCreation = CogUserQueueResultCreationConstants.Always;
            jm.UserResultAvailable += (o, ev) => f.BeginInvoke((Action)(() =>
            {
                ICogRecord r = jm.UserResult();
                if (r == null) return;
                string job = (string)r.SubRecords["JobName"].Content;
                var rs = (ICogRunStatus)r.SubRecords["RunStatus"].Content;
                ICogRecord lr = r.SubRecords["ShowLastRunRecordForUserQueue"].SubRecords["LastRun"];
                int idx = job.Contains("2") ? 1 : 0;
                Show(tiles[idx], lr, pickImage: true);
                Show(tiles[idx + 2], lr, pickImage: false);
                var keys = new List<string>();
                for (int k = 0; k < lr.SubRecords.Count; k++) keys.Add(lr.SubRecords[k].RecordKey);
                Log.Add(job + " " + rs.Result + " " + rs.TotalTime.ToString("0.0") + " ms; subrecords: " + string.Join(", ", keys));
                log.Items.Add(DateTime.Now.ToString("HH:mm:ss.fff") + " " + job + " " + rs.Result + " " + rs.TotalTime.ToString("0.0") + " ms");
            }));
            jm.Run();
        };
        f.FormClosing += (s, e) => { if (jm != null) jm.Shutdown(); };
        return f;
    }

    static void Show(CogRecordsDisplay d, ICogRecord lr, bool pickImage)
    {
        d.Subject = lr;
        // chọn record ảnh: khung trên = ảnh có overlay đầu tiên khác rỗng; khung dưới = record ảnh cuối
        string pick = null;
        for (int i = 0; i < lr.SubRecords.Count; i++)
        {
            var img = lr.SubRecords[i].Content as ICogImage;
            if (img == null || img.Width == 0) continue;
            pick = lr.RecordKey + "." + lr.SubRecords[i].RecordKey;
            if (pickImage) break;
        }
        if (pick != null) d.SelectedRecordKey = pick;
        d.Display.Fit(true);
    }

    // ---------- form Lệnh & job: CogToolBlockEditV2 nhúng trong vùng mockup cấp ----------
    static Form BuildToolBlockEdit()
    {
        var f = NewForm("Lệnh & job — spike", 1560, 820);
        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add("Bảng lệnh"); tabs.TabPages.Add("Job (ToolBlock)"); tabs.TabPages.Add("Kết quả trả về & giới hạn");
        tabs.SelectedIndex = 1;
        var page = tabs.TabPages[1];
        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 46 };
        bottom.Controls.Add(new Button { Text = "Chạy ảnh này", Left = 6, Top = 6, Width = 140, Height = 34 });
        bottom.Controls.Add(new Button { Text = "Chạy bộ ảnh có nhãn (42)", Left = 152, Top = 6, Width = 200, Height = 34 });
        bottom.Controls.Add(new Button { Text = "Lưu (sao lưu bản cũ)", Left = 1360, Top = 6, Width = 170, Height = 34 });
        var ed = new CogToolBlockEditV2 { Dock = DockStyle.Fill };
        if (Scene == "tbedit3") ed.Font = Control.DefaultFont;   // thử: không cho editor thừa hưởng font 9.75 của form
        page.Controls.Add(ed); page.Controls.Add(bottom);
        f.Controls.Add(tabs);
        f.Shown += (s, e) =>
        {
            var tb = MakeToolBlock();
            ed.Subject = tb;
            Log.Add("editor client " + ed.ClientSize + " min " + ed.MinimumSize);
            // liệt kê nút trên ToolBar bên trong editor; bản "tbedit2" ẩn nút mở/lưu/nạp file
            foreach (var bar in FindAll<ToolBar>(ed))
                foreach (ToolBarButton b in bar.Buttons)
                {
                    Log.Add("button '" + b.ToolTipText + "' style=" + b.Style);
                    string tip = (b.ToolTipText ?? "").ToLowerInvariant();
                    if (Scene != "tbedit" && (tip.Contains("protect") || tip.Contains("open") || tip.Contains("save") || tip.Contains("load") || tip.Contains("import") || tip.Contains("export") || tip.Contains("reset")))
                        b.Visible = false;
                }
        };
        return f;
    }

    static IEnumerable<T> FindAll<T>(Control root) where T : Control
    {
        foreach (Control c in root.Controls)
        {
            var t = c as T;
            if (t != null) yield return t;
            foreach (var x in FindAll<T>(c)) yield return x;
        }
    }

    // ToolBlock: nạp tool có sẵn từ job mẫu nếu có, gắn ảnh mẫu, chạy một lần để editor có LastRun
    static CogToolBlock MakeToolBlock()
    {
        string vpp = Vpp ?? @"C:\Program Files\Cognex\VisionPro\samples\QuickBuild\Count holes using ToolBlocks_Job_9000.vpp";
        object o = CogSerializer.LoadObjectFromFile(vpp);
        Log.Add("loaded " + o.GetType().FullName);
        CogToolBlock tb = o as CogToolBlock;
        CogJob job = o as CogJob;
        if (tb == null && job != null)
        {
            var tg = job.VisionTool as CogToolGroup;
            if (tg != null) foreach (ICogTool t in tg.Tools) { tb = t as CogToolBlock; if (tb != null) break; }
            if (tb == null && tg != null)
            {
                tb = new CogToolBlock();
                foreach (ICogTool t in tg.Tools) tb.Tools.Add(t);
            }
        }
        if (tb == null) tb = new CogToolBlock();
        // ảnh: lấy từ ImageFileTool của job (nếu job đọc file) hoặc ảnh mẫu VisionPro
        ICogImage img = null;
        if (img == null)
        {
            var f = new CogImageFile();
            f.Open(ImagePath ?? @"C:\Program Files\Cognex\VisionPro\Images\Fixture_Measurement_Demo.idb", CogImageFileModeConstants.Read);
            img = f[0]; f.Close();
        }
        if (tb.Inputs.Contains("InputImage")) tb.Inputs["InputImage"].Value = img;
        else Log.Add("toolblock inputs: " + tb.Inputs.Count);
        try { tb.Run(); Log.Add("tb run: " + tb.RunStatus.Result + " " + tb.RunStatus.Message); }
        catch (Exception ex) { Log.Add("tb run ex: " + ex.Message); }
        return tb;
    }

    // ---------- Configuration kiểu Application Wizard: nguyên QuickBuild trong một form ----------
    static Form BuildQuickBuildEdit()
    {
        var f = NewForm("Cấu hình (QuickBuild nhúng) — spike", 1560, 820);
        var ed = new CogJobManagerEdit { Dock = DockStyle.Fill };
        f.Controls.Add(ed);
        f.Shown += (s, e) =>
        {
            var jm = (CogJobManager)CogSerializer.LoadObjectFromFile(Vpp ?? @"C:\Program Files\Cognex\VisionPro\samples\ApplicationWizard\QuickBuild2Jobs\QuickBuild2Jobs.vpp");
            ed.Subject = jm;
            f.FormClosing += (o, ev) => jm.Shutdown();
        };
        return f;
    }

    // ---------- hiệu chuẩn: editor N-Point của VisionPro trong cột giữa form Hiệu chuẩn ----------
    static Form BuildCalib()
    {
        var f = NewForm("Hiệu chuẩn — spike", 1480, 800);
        var left = new ListBox { Dock = DockStyle.Left, Width = 250 };
        left.Items.AddRange(new object[] { "Calib-1 · 9 điểm · BottomCam", "Calib-2 · 11 điểm · BottomCam", "Calib-3 · ánh xạ · TopCam" });
        var ed = new CogCalibNPointToNPointEditV2 { Dock = DockStyle.Fill };
        var cal = new CogCalibNPointToNPointTool();
        double[,] p = { { -2, -2 }, { 0, -2 }, { 2, -2 }, { -2, 0 }, { 0, 0 }, { 2, 0 }, { -2, 2 }, { 0, 2 }, { 2, 2 } };
        for (int i = 0; i < 9; i++)
            cal.Calibration.AddPointPair(1224 + p[i, 0] * 231.4, 1024 + p[i, 1] * 230.9, 100 + p[i, 0], 50 + p[i, 1]);
        f.Controls.Add(ed); f.Controls.Add(left);
        f.Shown += (s, e) => { ed.Subject = cal; Log.Add("calib editor " + ed.ClientSize); };
        return f;
    }

    // ---------- camera: editor lấy ảnh của VisionPro (không có camera thật) ----------
    static Form BuildAcq()
    {
        var f = NewForm("Camera — spike", 1500, 760);
        var ed = new CogAcqFifoEditV2 { Dock = DockStyle.Fill };
        f.Controls.Add(ed);
        f.Shown += (s, e) => { ed.Subject = new CogAcqFifoTool(); Log.Add("acq editor " + ed.ClientSize); };
        return f;
    }
}
