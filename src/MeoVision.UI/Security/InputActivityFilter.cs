using System.Drawing;
using System.Windows.Forms;
using MeoVision.App.Security;

namespace MeoVision.UI.Security;

/// <summary>
/// Thao tác trên MeoVision làm mới đồng hồ tự đăng xuất (Chương 23, mục 23.2). Lọc ở cấp ứng dụng
/// nên không form nào phải tự báo. Chỉ tính khi một cửa sổ của MeoVision đang được chọn — chạy thật
/// thấy chuột lướt qua cửa sổ lúc người ta đang dùng chương trình khác cũng làm mới đồng hồ, mà đó
/// không phải "đang dùng máy". Với chuột, thêm điều kiện con trỏ THẬT SỰ đổi chỗ — phòng trường hợp
/// <c>WM_MOUSEMOVE</c> đến khi control dưới một con trỏ đứng yên được tạo lại.
/// </summary>
public sealed class InputActivityFilter : IMessageFilter
{
    private const int WmKeyDown = 0x0100;
    private const int WmMouseMove = 0x0200;
    private const int WmLButtonDown = 0x0201;
    private const int WmRButtonDown = 0x0204;
    private const int WmMouseWheel = 0x020A;
    private const int WmPointerDown = 0x0246;

    private readonly Session _session;
    private Point _lastMouse = new(int.MinValue, int.MinValue);

    public InputActivityFilter(Session session) => _session = session;

    public bool PreFilterMessage(ref Message m)
    {
        switch (m.Msg)
        {
            case WmMouseMove:
                Point now = Control.MousePosition;
                if (now != _lastMouse && Form.ActiveForm is not null)
                {
                    _lastMouse = now;
                    _session.Touch();
                }
                break;
            case WmKeyDown:
            case WmLButtonDown:
            case WmRButtonDown:
            case WmMouseWheel:
            case WmPointerDown:
                if (Form.ActiveForm is not null)
                {
                    _session.Touch();
                }
                break;
        }
        return false;   // chỉ nghe, không chặn
    }
}
