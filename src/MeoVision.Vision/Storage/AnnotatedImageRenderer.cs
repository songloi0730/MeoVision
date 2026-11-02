using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Cognex.VisionPro;
using Cognex.VisionPro.Display;
using MeoVision.Core;

namespace MeoVision.Vision.Storage;

/// <summary>
/// Dựng ảnh KÈM đồ hoạ kết quả từ record của job — ảnh mà người vận hành thấy trên màn hình, để QA
/// mở bằng trình xem ảnh thường (như "ảnh màn hình" của hai chương trình tham khảo). Display của
/// VisionPro là control ActiveX: chỉ dựng được trên một luồng STA có vòng thông điệp, nên bộ dựng có
/// luồng riêng với một form ẩn; người gọi (luồng ghi kết quả) chờ qua <see cref="Control.Invoke"/>.
/// </summary>
public sealed class AnnotatedImageRenderer : IDisposable
{
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new();
    private Form? _host;
    private CogRecordDisplay? _display;

    public AnnotatedImageRenderer(int width = 1280, int height = 1024)
    {
        _thread = new Thread(() =>
        {
            _host = new HiddenHost
            {
                ShowInTaskbar = false, FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.Manual, Location = new Point(-32000, -32000),
                Size = new Size(width, height), Opacity = 0,
            };
            _display = new CogRecordDisplay { Dock = DockStyle.Fill };
            _host.Controls.Add(_display);
            _host.Shown += (_, _) => _ready.Set();
            Application.Run(_host);
        }) { IsBackground = true, Name = "AnnotatedImageRenderer" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    /// <summary>
    /// Ghi ảnh của record con có đồ hoạ (khoá <paramref name="preferredKey"/> nếu có) ra JPEG.
    /// Không có ảnh nào trong record thì không ghi gì và trả false.
    /// </summary>
    public bool Render(ICogRecord record, string preferredKey, string file)
    {
        Guard.ThrowIfNull(record, nameof(record));
        Guard.ThrowIfNull(file, nameof(file));
        if (!_ready.Wait(TimeSpan.FromSeconds(10)))
        {
            throw new InvalidOperationException("Renderer did not start");
        }
        ICogRecord? target = Find(record, preferredKey);
        if (target is null)
        {
            return false;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        string temp = file + ".tmp";
        _host!.Invoke(new Action(() =>
        {
            _display!.Record = target;
            _display.Fit(true);
            using Image content = _display.CreateContentBitmap(
                CogDisplayContentBitmapConstants.Image, null, 0);
            content.Save(temp, ImageFormat.Jpeg);
            _display.Record = null;
        }));
        if (File.Exists(file))
        {
            File.Delete(file);
        }
        File.Move(temp, file);
        return true;
    }

    public void Dispose()
    {
        if (_host is not null && _ready.IsSet)
        {
            _host.BeginInvoke(new Action(() => _host.Close()));
        }
        _thread.Join(TimeSpan.FromSeconds(5));
        _ready.Dispose();
    }

    /// <summary>Record con: đúng khoá nếu có; không thì ảnh đầu tiên có đồ hoạ; không thì ảnh đầu.</summary>
    /// <summary>
    /// Đo thật (session 23): form Opacity 0 vẫn là cửa sổ "hiện" — lúc Application.Run nó được kích
    /// hoạt, cướp focus của màn chính một lần, và có trong Alt+Tab. Không kích hoạt + tool window.
    /// </summary>
    private sealed class HiddenHost : Form
    {
        private const int ToolWindow = 0x00000080;   // WS_EX_TOOLWINDOW: không có trong Alt+Tab
        private const int NoActivate = 0x08000000;   // WS_EX_NOACTIVATE

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams p = base.CreateParams;
                p.ExStyle |= ToolWindow | NoActivate;
                return p;
            }
        }
    }

    private static ICogRecord? Find(ICogRecord record, string preferredKey)
    {
        ICogRecord? withGraphics = null;
        ICogRecord? anyImage = null;
        for (int i = 0; i < record.SubRecords.Count; i++)
        {
            ICogRecord sub = record.SubRecords[i];
            if (sub.Content is not ICogImage image || image.Width == 0)
            {
                continue;
            }
            if (preferredKey.Length > 0 &&
                string.Equals(sub.RecordKey, preferredKey, StringComparison.Ordinal))
            {
                return sub;
            }
            withGraphics ??= sub.SubRecords.Count > 0 ? sub : null;
            anyImage ??= sub;
        }
        return withGraphics ?? anyImage;
    }
}
