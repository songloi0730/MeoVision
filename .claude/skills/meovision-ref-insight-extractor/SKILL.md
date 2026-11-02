---
name: meovision-ref-insight-extractor
description: Dùng khi cần review thêm một dự án vision C#/.NET tham khảo bên ngoài (khác RefVis-A đã có) để lấy insight bổ sung cho MeoVision hoặc sách. Kích hoạt khi tác giả nói "review dự án vision này", "đây là dự án tham khảo mới", hoặc đưa đường dẫn tới một project vision thật khác.
---

# MeoVision reference-project insight extractor

Bản rút gọn của skill `project-insight-extractor` (dự án sách "C# cho Automation Machine") — áp
dụng cho dự án vision tham khảo mới, output map vào 16 chương sách VisionPro thay vì 19 chương.

## Bước 0 — Bắt buộc trước tiên
Đọc `VisionPro_Image_Processing_Book/docs/private/RefVis_Alias_Map.md`. Nếu dự án đã có bí danh
→ dùng lại. Nếu mới → gán `RefVis-B`, `RefVis-C`... theo thứ tự, cập nhật bảng NGAY bằng Edit
tool trước khi đọc code.

**Từ đây trở đi: không nhắc tên thật (thư mục/công ty/sản phẩm) ở bất kỳ đâu ngoài
`RefVis_Alias_Map.md`** — kể cả trong báo cáo cho tác giả.

## Bước 1 — Giao cho Agent, không đọc trực tiếp
Dùng Agent tool (`subagent_type: general-purpose` — KHÔNG dùng `Explore` vì cần quyền Write).
Brief phải tự chứa: đường dẫn thật, bí danh `RefVis-X` cần dùng, đường dẫn file output cần ghi
(`VisionPro_Image_Processing_Book/docs/private/project-reviews/Review_RefVis-X.md`), yêu cầu ẩn
danh cứng, và cấu trúc 12 mục output (xem file `Review_RefVis-A.md` đã có làm mẫu — copy đúng cấu
trúc, đổi mục 2/3 để tập trung đúng insight liên quan MeoVision: kiến trúc IVisionEngine, IPC,
recipe model, thay vì insight chung chung).

Agent tự ghi file ra đĩa, chỉ trả tóm tắt ngắn (~150 từ) về context chính.

## Bước 2 — Kiểm tra ẩn danh lần cuối
```bash
grep -inE "<tên thật cần kiểm>" VisionPro_Image_Processing_Book/docs/private/project-reviews/Review_RefVis-X.md
```
Grep ra dòng nào → sửa ngay trước khi báo hoàn thành.

## Bước 3 — Cập nhật design-note nếu insight đủ quan trọng
Nếu insight ảnh hưởng quyết định kiến trúc đã chốt trong `docs/design-notes/DESIGN_LOG.md` (đặc
biệt mục [1] bối cảnh gốc) — **thêm một mục `## [N] YYYY-MM-DD — Tiêu đề` MỚI vào CUỐI file đó**
(không sửa mục cũ, không tạo file `NNNN-*.md` riêng — quy ước đã đổi từ 2026-07-25, xem header của
`DESIGN_LOG.md`), tham chiếu ngược lại mục liên quan, theo đúng tinh thần ADR (phương án + đánh
đổi + lý do).
