# MeoVision — Đặc tả giao diện v2.2 (2026-10-25)

Mockup tương tác: `docs/ui/meovision_ui_mockup_v2.html` (cửa sổ 1920×1080, mở bằng trình duyệt).
Lý do và các phương án đã cân nhắc: `docs/design-notes/DESIGN_LOG.md` mục [15], [16], [20].
**v2.2:** phân tích theo tình huống sử dụng (`docs/ui/UX_Workflows_v2.2.md`) → không gian **Thiết lập lệnh**, bản nháp,
bộ ảnh thử, tham số nhanh, đường tắt; form Camera lùi về cấp máy. Bản 2.1 của mockup giữ ở `meovision_ui_mockup_v2.1.html`.
**Thay cho v1** (`UI_Spec_v1.md` giữ lại để đối chiếu). v1 lấy chuẩn HMI của một dự án cá nhân chưa kiểm
chứng ngoài thực tế; v2 lấy chuẩn từ các phần mềm vision đã **chạy thật** trên máy này.

## 1. Bằng chứng

| Nguồn | Cách lấy | Lấy gì |
|---|---|---|
| RefVis-A (phần mềm vision 4 camera, WinForms, VisionPro) | Chạy exe thật, chụp màn chính; hiện 16 form ngoài màn hình để chụp | Thanh công cụ biểu tượng; lưới 6 khung ảnh đặt tên theo chức năng; tab Log/ManualRun bên phải, dòng lỗi đỏ; ô trạng thái thiết bị đỏ/xanh ở đáy; form quản lý mã sản phẩm, tham số hệ thống, camera (nhúng editor lấy ảnh), ToolBlock (nhúng editor), hiệu chuẩn, dữ liệu bù, đèn, đăng nhập |
| RefVis-C (khung vision của nhà tích hợp, WinForms, VisionPro) | Chạy exe thật (quyền admin, tác giả mở), chụp màn chính + 41 ảnh form | Thanh công cụ biểu tượng 9 nút; 3 khung ảnh có thanh zoom/kéo/lưới + dòng toạ độ/zoom/giá trị; dock tự ẩn Log + Điều khiển tay; thanh trạng thái sản phẩm/người dùng/số server/số kết nối/CPU/RAM/giờ; Cài đặt 8 tab dạng bảng + cột nút chung; Giao tiếp (cây server, nhận/gửi, Hex, gửi lặp); vision là TCP server cho PC control cùng máy; đăng nhập chọn vai trò + mật khẩu, tự đăng xuất 10 phút; đổi ngôn ngữ khi chạy |
| 11 repo vision trên GitHub (VisionPro, Halcon, OpenCV; ô tô, tản nhiệt, AOI, lát cắt, khung máy…) | Clone mã nguồn, quét mọi `*.Designer.cs` (form, tab, nút, control vision, giao tiếp) | Đèn Camera/PLC/Đèn; bảng bit bắt tay PLC (READY/START/END/NG/ERR, Keep Alive); bộ đếm OK/NG/tỷ lệ theo camera + nút xoá đếm; tra cứu lịch sử theo ngày/serial/kết quả/ca; TCP debug hex + gửi lặp; chế độ Online/Thử một lần/Thử cục bộ |

Ảnh chụp và bảng kiểm kê nằm ở thư mục ghi chú riêng của dự án sách (không commit).

## 2. Ràng buộc đã chốt với tác giả

1. **WinForms.**
2. **Vision là phần mềm riêng**, giao tiếp với PLC, hoặc với phần mềm PC control (cùng máy tính, hoặc hai máy
   tính: một máy vision, một máy PC control).
3. **Ba vai trò**: Vận hành / Kỹ sư / Quản trị (bỏ Trưởng ca — phần mềm công nghiệp thực tế không có).
4. Không lấy chuẩn HMI của AM.AutoFrame làm gốc.

## 3. Khung cửa sổ chính

```
Thanh tiêu đề   MeoVision 1.0 — <máy> — <sản phẩm>
Thanh công cụ   [Trang chính][Camera & đèn][Hiệu chuẩn][Thiết lập lệnh][Giao tiếp][Sản phẩm][Lịch sử][Cài đặt] | [Đăng nhập][Thoát]
  84 px          … bên phải: Sản phẩm (chữ lớn) · ngôn ngữ · nút ONLINE/OFFLINE (nhấn giữ 1 s)
Vùng giữa       Lưới N khung ảnh (N, số cột: Cài đặt › Hiển thị). Mỗi khung:
                  đầu: tên chức năng · lệnh · camera · [⚙ Chỉnh lệnh] (Kỹ sư) · ms · thẻ OK/NG
                  thanh công cụ: chọn/kéo/phóng/thu/vừa khung/lưới/ảnh gốc↔kết quả · lý do NG
                  ảnh + nguồn ảnh + overlay số đo
                  đáy: toạ độ · zoom · giá trị pixel · dải 16 kết quả gần nhất (bấm = ảnh đó lên khung)
Dock phải 470   tab Nhật ký (bộ đếm OK/NG/tỷ lệ, lọc Tất cả/Lỗi/Lệnh, dòng lỗi đỏ) | Chạy tay | Bộ đếm
Thanh trạng thái  sản phẩm · người dùng (+ đếm ngược tự đăng xuất) · ô màu từng thiết bị theo vai trò ·
                  ô kết nối (server/client/PLC + nhịp tim) · ổ đĩa · CPU · RAM · giờ
```

Form chức năng là **cửa sổ riêng, không modal**, mở từ thanh công cụ, nằm trên màn chính (giống RefVis-A/C).
Màn chính và việc trả lời lệnh vẫn chạy phía sau. Mỗi form chỉ mở một bản.

## 4. Danh mục form

| Form | Vai trò mở | Nội dung | Chương sách |
|---|---|---|---|
| Khởi động | — (tự hiện) | 7 bước: thư mục dữ liệu, file ngôn ngữ, VisionPro, cấu hình máy, thiết bị (đã có, Ch17–18) + nạp job theo sản phẩm (tiến độ từng lệnh), mở kênh (Ch19–21); kết quả bằng chữ + thời gian; bước bắt buộc hỏng → dừng, nói lý do; thiết bị lỗi → mở nhưng không tự ONLINE | 17–21 |
| Camera & đèn (cấp MÁY) | Kỹ sư | Tab Live & đèn: thiết bị theo vai trò + trạng thái + nối lại; live, histogram, **độ nét** (giá trị tốt nhất đã đạt), lưới căn tâm; phơi sáng/gain **mặc định** (`machine.json`); thử đèn — cùng một màn · tab Editor VisionPro (`CogAcqFifoEditV2`) | 18, 22 |
| Hiệu chuẩn | Kỹ sư | Danh sách hiệu chuẩn của máy; tab N-Point (tiến trình 5 bước máy dẫn, bảng điểm + sai số từng điểm), Tâm xoay, Ánh xạ 2 camera, Kết quả (ngày, người, RMS + ngưỡng, kích thước pixel, hạn kiểm lại), Editor VisionPro (`CogCalibNPointToNPointEditV2`) | 21 |
| Thiết lập lệnh | Kỹ sư | Trái: Bảng lệnh (lệnh → kênh, camera, job, hiệu chuẩn, khung, mẫu trả lời, timeout, thử lại) + từng lệnh. Giữa: nguồn ảnh (Live / Chụp 1 ảnh — chỉ OFFLINE; Ảnh gần nhất = 8 ảnh/lệnh trong RAM + ảnh đã lưu; Ảnh NG lịch sử; Thư mục) + dải ảnh + kết quả job. Phải, 4 bước đi tự do: **Ảnh & đèn** (phơi sáng thang log, đèn của lệnh, xám TB / % cháy sáng / độ nét / điểm job, histogram, tô vùng cháy, chụp loạt 3×3, tự chỉnh một lần) · **Job** (tham số nhanh = terminal input của ToolBlock; editor đầy đủ trên bản nháp; dạy lại mẫu) · **Kết quả & giới hạn** (dưới/trên/bù/định dạng + phân bố trên bộ ảnh thử, đếm OK→NG và NG lọt) · **Bộ ảnh thử** (nhãn OK/NG, bản đang chạy so với bản nháp). Đáy: dải **Chạy kèm** (khi ONLINE: số sản phẩm thật, giống/khác, chênh ms, bỏ lượt, xem ảnh khác) + bản nháp + Xem khác biệt / Huỷ / Quay lại bản trước / Áp dụng (giữa hai lệnh) | 19, 20, 22 |
| Giao tiếp | Kỹ sư | Tab Kết nối (TCP server / TCP client / PLC; địa chỉ; client cho phép; nhịp tim; bảng bit bắt tay PLC trực tiếp) · Giao thức (bản tin mẫu) · Thử gửi/nhận (log thời gian + hướng, Hex, gửi, lặp) | 21 |
| Sản phẩm | Vận hành (chỉ xem), Kỹ sư (chuyển/tạo/sửa/xoá) | Bảng sản phẩm + cột Kiểm tra thiết bị; Chuyển sang / Tạo từ bản sao / Đổi tên / Xoá / Mở thư mục / Khôi phục bản cũ | 19 |
| Lịch sử | Mọi vai trò | Lọc thời gian/lệnh/kết quả/serial; bảng + ảnh gốc kèm overlay; **bản sản phẩm đã dùng** + khác biệt với bản hiện tại; mở thư mục ảnh; Kỹ sư: chạy lại với job hiện tại, đánh dấu NG giả, thêm vào bộ ảnh thử (OK/NG), mở trong Thiết lập lệnh, gói chẩn đoán | 22, 23 |
| Cài đặt | Quản trị | Tab Hiển thị, Thiết bị (`machine.json`), Lưu ảnh, Lưu dữ liệu, Người dùng, Hệ thống; cột nút Áp dụng/Lưu/Hoàn tác/Về mặc định | 23, 24 |
| Đăng nhập | — | Chọn vai trò + mật khẩu, đổi mật khẩu | 23 |

## 5. Giao tiếp với máy

**Kênh** (DESIGN_LOG [17], đã chốt): danh sách kênh khai ở cấp máy (`machine.json`); mỗi lệnh trong bảng lệnh của
sản phẩm chọn một kênh (cột "Kênh"). Kênh = **một cổng TCP** (PC control) hoặc **một vùng bắt tay D/M** (PLC).
Máy một trạm, hoặc dùng các camera lần lượt → 1 kênh. Các trạm có thể chụp cùng lúc → mỗi trạm 1 kênh (với PLC là
bắt buộc: một vùng bắt tay chỉ xử lý một lần chụp mỗi lúc). Card mạng của camera không liên quan tới kênh. Lệnh
đến sai kênh → `Tn,ERR,CHANNEL`; sản phẩm dùng kênh máy không có → không kích hoạt được; mỗi kênh có nhịp tim và
một ô trạng thái riêng; lệnh hệ thống `SW`, `HB` nhận trên mọi kênh.

| Kiểu kênh | Khi dùng | Ghi chú |
|---|---|---|
| TCP server, PC control cùng máy | Một IPC chạy cả PC control và vision | Nghe 127.0.0.1; ví dụ CH1 = 10001, CH2 = 10002 |
| TCP server, PC control máy khác | Hai máy tính | Nghe trên card mạng điều khiển; chỉ nhận client đã khai báo; mở cổng tường lửa |
| PLC trực tiếp | Không có PC control | **Mitsubishi MC 3E (SLMP) làm trước** (tác giả chốt), Modbus TCP sau; một kết nối MC dùng chung, mỗi kênh một vùng (vd CH1: D100–D130 + M100–M108; CH2: D200–D230 + M200–M208 — mã lệnh, serial 20 ký tự, kết quả số thực 32 bit, nhịp tim); bit đặt tên theo NDM (Trigger, Trigger Ack, Acquiring, Inspection Complete, Results Valid, Online, Error, Job Change Request, Set Offline) + thanh ghi mã lệnh, serial, kết quả; nhịp tim (§8) |
| NDM qua card Cognex CC24 | Máy có card CC24 | Thư viện `Cognex.VisionPro.Comm` (EtherNet/IP, PROFINET, SLMP) — chưa làm khi chưa có card |

Bản tin (TCP): `Tn,<serial>\r` → `Tn,OK,<out1>,<out2>…\r` | `Tn,NG,<giá trị>\r` | `Tn,ERR,<mã>\r`;
`SW,<sản phẩm>` đổi sản phẩm; `HB` nhịp tim. **NG** = đo được nhưng không đạt; **ERR** = không đo được
(camera, job, offline, timeout). OFFLINE trả `ERR,OFFLINE` ngay để máy không chờ timeout.

## 6. Quyền

| Việc | Vận hành | Kỹ sư | Quản trị |
|---|---|---|---|
| Xem trang chính, nhật ký, bộ đếm, lịch sử | ✓ | ✓ | ✓ |
| Đặt lại bộ đếm | ✓ | ✓ | ✓ |
| Chuyển sang sản phẩm có sẵn (máy vẫn đổi được bằng lệnh `SW`) | — | ✓ | ✓ |
| Chạy tay, gửi thử, live/chụp thử (khi OFFLINE) | — | ✓ | ✓ |
| Camera & đèn, hiệu chuẩn, thiết lập lệnh, giao tiếp; tạo/sửa sản phẩm | — | ✓ | ✓ |
| Sửa bản nháp khi ONLINE, chạy kèm trên sản phẩm thật, áp dụng / quay lại giữa hai lệnh (chốt 2026-10-25) | — | ✓ | ✓ |
| ONLINE ↔ OFFLINE; thoát chương trình | — | ✓ | ✓ |
| Cài đặt hệ thống, người dùng | — | — | ✓ |

Vận hành không cần mật khẩu. Kỹ sư/Quản trị tự đăng xuất về Vận hành sau 10 phút không thao tác. Không khoá
tài khoản khi nhập sai.

## 7. Quy tắc tương tác

1. Nút thiếu quyền hoặc bị khoá vẫn hiện: mờ + biểu tượng khoá; bấm vào nói lý do (cần vai trò nào / đang ONLINE).
2. ONLINE khoá live, chụp thử, chụp loạt, gửi thử, chạy tay trên ảnh camera (camera thuộc về lệnh của máy).
   Chạy trên ảnh đã ghi (ảnh gần nhất, lịch sử, thư mục) vẫn được. **Sửa khi ONLINE (tác giả chốt 2026-10-25):**
   sửa cấu hình của sản phẩm đi vào **bản nháp**; **chạy kèm**: mỗi sản phẩm thật được trả lời bằng bản đang chạy rồi
   chạy thêm trên bản nháp để so (không làm chậm câu trả lời, bận thì bỏ lượt); "Áp dụng" = lưu có sao lưu + nạp lại
   sản phẩm, đổi job giữa hai lệnh (bộ máy chạy lệnh, Chương 20), không bắt buộc bộ ảnh thử, tự ghi ai/lúc nào/khác
   biệt; "Quay lại bản trước" một chạm. Cài đặt hệ thống và thiết bị vẫn khoá khi ONLINE.
3. Màu chỉ dùng cho OK/NG, trạng thái thiết bị, ONLINE/OFFLINE. Phần còn lại xám kiểu Windows.
4. NG luôn kèm lý do (mục kiểm, giá trị đo, giới hạn) trên khung ảnh, trong nhật ký và lịch sử.
5. Mất thiết bị: khung của lệnh dùng thiết bị đó nói rõ đang thử lại lần mấy, chờ bao lâu, lệnh sẽ trả gì.
   Các khung khác vẫn chạy.
6. Mọi lưu thay đổi (bảng lệnh, giới hạn, job, hiệu chuẩn, sản phẩm): sao lưu bản cũ + ghi ai/lúc nào.
7. Chuỗi giao diện qua `ILocalizer` (vi/en/zh, đổi khi chạy). Mã lệnh, vai trò thiết bị, tên file không dịch.
8. Kích thước cho cảm ứng: nút thanh công cụ 84×76, nút lệnh chính ≥ 52 px, dòng bảng ≥ 30 px.
9. Đường tắt theo ngữ cảnh (v2.2): ⚙ trên khung → Thiết lập lệnh của lệnh đó với ảnh đang hiện; ô dải 16 kết quả →
   ảnh đó lên khung; dòng NG trong nhật ký → Lịch sử; ô thiết bị → Camera & đèn; ô kênh → Giao tiếp (Thử gửi/nhận).
10. Nơi lưu luôn ghi rõ trên màn: giá trị của lệnh → `product.json`; mặc định của camera → `machine.json`.

## 8. Control VisionPro trong giao diện (v2.1 — đã nhúng thử thật, `docs/ui/spike/`)

Nguồn: ứng dụng do Application Wizard của VisionPro sinh ra (đã build và chạy), QuickBuild, mẫu
`samples/Programming`, các repo GitHub dùng VisionPro, và spike `UiSpike.cs` nhúng control thật đúng kích
thước mockup trên màn 1920×1080.

| Chỗ | Control | Cách dùng trong MeoVision |
|---|---|---|
| Khung ảnh màn chính | `CogRecordsDisplay` | `ShowRecordsDropDown = false`, tắt `HorizontalScrollBar`/`VerticalScrollBar` cho Vận hành; record hiển thị (kết quả / gốc) khai báo theo lệnh trong `product.json`; Kỹ sư bật lại combobox record (tên gốc VisionPro, không dịch). Nền navy quanh ảnh là cố định (`BackColor` không đổi được — đã thử) |
| Form Lệnh & job, tab Job | `CogToolBlockEditV2` | Nhúng nguyên trên **bản sao**; ẩn Open / Save / Save As / Reset / Protect-with-password bằng cách duyệt `ToolBar` con; lưu chỉ qua nút của MeoVision (sao lưu + nhật ký) |
| Form Camera | `CogAcqFifoEditV2` | Nhúng nguyên (như RefVis-A và repo GitHub), ẩn nút file; nút "Ghi phơi sáng + đèn vào lệnh" |
| Form Hiệu chuẩn, tab Editor VisionPro | `CogCalibNPointToNPointEditV2` | Kiểm tra / chỉnh tay trên cùng đối tượng hiệu chuẩn mà luồng máy dẫn tạo |
| Không dùng | `CogJobManagerEdit` (QuickBuild nhúng, cách của Application Wizard) | Có mở/lưu file, Navigator, Posted Items riêng → vượt qua thư mục sản phẩm và sao lưu |

Quy tắc kỹ thuật rút từ spike:
1. **Không để control Cognex thừa hưởng font của form.** Đặt `Font = Control.DefaultFont` cho từng editor
   (font 9.75 làm dòng trạng thái của editor chồng chữ). Font lớn của MeoVision đặt trên control của mình.
2. Chữ trong editor là tiếng Anh (theo bản VisionPro cài) — không qua `ILocalizer`; ghi chú điều này trên form.
3. Máy phát triển chạy tỉ lệ 125 %: process DPI-aware vẫn dùng được control Cognex nếu theo quy tắc 1. IPC đích
   khuyến nghị 100 %.
4. Nạp job lúc khởi động có thể mất 14–18 s (đo: file QuickBuild 2 job, lần chạm VisionPro đầu) → màn chờ có
   tiến độ từng job.
5. Ảnh 2448×2048 (tỉ lệ 1.2) trong khung 2×2 với dock 470 px chỉ dùng ~65 % bề ngang khung → dock 380 px và
   thu gọn được; số cột khung chỉnh ở Cài đặt › Hiển thị.
6. Tuỳ chọn "Vẽ ảnh khi chạy: mọi lần / chỉ NG / tắt" (Application Wizard có "enable display").

### PLC và thư viện NDM của VisionPro

VisionPro có thư viện NDM (`Cognex.VisionPro.Comm`: EtherNet/IP, PROFINET, SLMP) với sẵn bắt tay Trigger /
Inspection Complete / Job Change Request / Offline Request / Soft Event và control `CogSlmpConfigurator`.
**Chỉ tạo được qua card Cognex CC24** (`CogCommCard.FfpAccess.CreateNetworkDataModel*`; máy này 0 card).
Vì vậy kiểu "PLC trực tiếp" mặc định do MeoVision tự làm (MC 3E / Modbus TCP) nhưng **đặt tên tín hiệu theo
NDM**; có card thì thêm kiểu "NDM" dùng thư viện Cognex.

## 9. Đã chốt / còn mở

- ✅ (2026-10-16) Vận hành **không** được chuyển sản phẩm; chỉ Kỹ sư hoặc máy (lệnh `SW`).
- ✅ (2026-10-16) PLC: **Mitsubishi MC 3E (SLMP) làm trước**, Modbus TCP sau.
- ✅ (2026-10-16) Giao tiếp theo **kênh** khai ở cấp máy, mỗi lệnh chọn kênh (DESIGN_LOG [17]) — sau khi khảo
  sát RefVis-A (1 cổng), RefVis-C (8 cổng theo trạm), QuickBuild (thiết bị cấp ứng dụng, job dùng chung).
- ✅ (2026-10-25) **Cho sửa khi ONLINE**: bản nháp + chạy kèm trên sản phẩm thật + áp dụng/quay lại giữa hai lệnh
  (`UX_Workflows_v2.2.md` D2, D2b).
- ✅ (2026-10-25) **Bỏ** kiểm mẫu chuẩn đầu ca.
- ✅ (2026-10-25) Ảnh: giữ ít trong RAM (mặc định 8 ảnh/lệnh), còn lại đọc từ ảnh đã lưu.
