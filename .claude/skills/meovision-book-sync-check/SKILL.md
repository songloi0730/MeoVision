---
name: meovision-book-sync-check
description: Dùng SAU KHI đổi code MeoVision theo cách có thể ảnh hưởng ví dụ đã in trong sách VisionPro (đổi tên class/method/API, đổi luồng dữ liệu, đổi kiến trúc). Kích hoạt khi tác giả nói "kiểm tra sách có khớp code không", "code này có đúng như sách dạy không", hoặc trước khi báo hoàn thành bất kỳ thay đổi kiến trúc nào.
---

# MeoVision ↔ book sync check

> Từ `docs/design-notes/DESIGN_LOG.md` mục [4]: **code là phần mềm thật trước, sách cập nhật theo
> code sau** — đảo ngược nguyên tắc ban đầu của skill này (sách không còn là nguồn sự thật mặc
> định). Xem mục [4] phần "5 điểm code khác sách hiện tại" cho danh sách lệch đã biết (đã áp dụng
> vào Chương 14/15 ở Session 18 sách — nếu phát hiện lệch MỚI, đó là việc của lần chạy skill này).

Đổi code ở đây mà không kiểm tra có thể khiến sách và code lệch thêm mà không ai biết.

## Quy trình
1. Đọc `reference/Outline_To_Book_Mapping.md` — tìm project/class vừa đổi tương ứng chương/mục
   nào.
2. Đọc đúng file `output/Chapter_NN_*.md` đó trong
   `C:\Users\dinhn\OneDrive\Study\Chung\VisionPro_Image_Processing_Book` — so sánh tên class,
   tên method, luồng dữ liệu mô tả trong Code X.Y của sách với code thật vừa đổi.
3. Nếu lệch: **KHÔNG tự ý sửa file sách từ đây** — báo lại tác giả cụ thể chỗ lệch (file:dòng ở cả
   2 phía). **Mặc định giả định code đúng** (`DESIGN_LOG.md` mục [4]) — trình bày như "sách cần cập
   nhật theo code thế này", không phải "sách hay code, cái nào đúng?" trung lập như trước — trừ khi
   lệch đó rõ ràng là lỗi thật trong code (không phải chủ đích thiết kế), thì nói rõ điều đó thay
   vì mặc định.
   Việc sửa file sách thực hiện ở phiên làm việc trên dự án sách, không phải từ đây.
4. Nếu khớp: xác nhận ngắn gọn, cập nhật `reference/Outline_To_Book_Mapping.md` nếu có
   project/class mới chưa được liệt kê.
