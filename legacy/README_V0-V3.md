# MeoVision

Chương trình xử lý ảnh công nghiệp minh hoạ cho sách **"Xử lý ảnh công nghiệp với Cognex
VisionPro"**. Trạm kiểm tra kích thước + đọc mã + bắt vị trí cho máy lắp ráp — ứng dụng mẫu
xuyên suốt 16 chương của sách.

- Toàn bộ quyết định kiến trúc (1 file duy nhất, theo thời gian): [`docs/design-notes/DESIGN_LOG.md`](docs/design-notes/DESIGN_LOG.md)
  (kiến trúc hiện hành = mục [2]; bối cảnh nghiên cứu gốc = mục [1])
- Trạng thái hiện tại: [`PROJECT_STATUS.md`](PROJECT_STATUS.md)
- Quy tắc code cho AI assistant: [`CLAUDE.md`](CLAUDE.md)

## Trạng thái

🟢 **V0-V3 đã có code thật, build/test/chạy thật** (net48, MSBuild + vstest.console.exe) — xem
`PROJECT_STATUS.md` để biết chi tiết bằng chứng và bước tiếp theo.

## Yêu cầu

- **.NET Framework 4.8 Developer Pack** — toàn bộ solution target net48, WinForms, 1 process
  (đã đổi từ đề xuất ban đầu .NET 9/WPF/IPC sau phản biện đúng của tác giả — xem `DESIGN_LOG.md`
  mục [2]).
- Visual Studio (Professional/Community) khuyến nghị cho WinForms Designer.
- Cognex VisionPro 9.0 CR2 x64 (đã cài trên máy phát triển) — chỉ cần khi build/chạy
  `MeoVision.Engine` thật; các project khác chạy được không cần VisionPro (dùng
  `MeoVision.Engine.Simulated`).
