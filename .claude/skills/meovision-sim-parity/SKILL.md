---
name: meovision-sim-parity
description: Dùng khi thêm tính năng mới vào IVisionEngine hoặc bất kỳ interface nào trong MeoVision.Core.Abstractions. Kích hoạt khi tác giả nói "thêm method vào IVisionEngine", "IVisionEngine cần thêm...", hoặc sau khi sửa MeoVision.Engine (bản thật) mà chưa đụng đến MeoVision.Engine.Simulated.
---

# MeoVision simulation parity

Nguyên tắc "simulation parity" (đã chứng minh hiệu quả ở AM.AutoFrame): **mọi capability trên
`IVisionEngine` phải có cả bản thật (`MeoVision.Engine`) và bản giả lập
(`MeoVision.Engine.Simulated`)** — không được để Simulated tụt lại sau Real.

## Quy tắc
1. Thêm method/property mới vào `IVisionEngine` → viết implementation ở CẢ 2 project trong cùng
   một lần sửa. Nếu chỉ có thời gian làm 1 bên, làm **Simulated trước** (đây là thứ chạy được
   trên mọi máy đọc sách, kể cả không có license VisionPro).
2. `SimulatedVisionEngine` đọc từ bộ ảnh mẫu trong `assets/` (golden set OK/NG) — không code cứng
   giá trị trả về, để demo có tính thuyết phục (score/toạ độ khác nhau giữa các lần chạy trong
   phạm vi hợp lý).
3. Trước khi báo "xong" một tính năng: build cả 2 project, xác nhận `MeoVision.Application` chạy
   được giống hệt logic dù trỏ vào Engine nào (đây chính là điểm chứng minh interface đúng).
