# MeoVision — Đặc tả giao diện v1 (2026-10-16)

Mockup tương tác: `docs/ui/meovision_ui_mockup_v1.html` (mở bằng trình duyệt, tỷ lệ thật 1920×1080).
Quyết định và phương án đã cân nhắc: `docs/design-notes/DESIGN_LOG.md` mục [14].

## 1. Nguồn đã tham khảo

| Nguồn | Lấy gì |
|---|---|
| Chuẩn HMI AM.AutoFrame (`AM.AutoFrame/docs/HMI_Master_Index.md`, `HMI_UI_Architecture_Template_v3.md`) | Khung 4 vùng, token màu ISA-101, kích thước chạm, 4 vai trò, "giải thích thay vì giấu", empty state, màu chỉ khi có ý nghĩa, wizard hiệu chuẩn 2 nhánh |
| RefVis-A (phần mềm vision một dòng máy, 21 form) | Danh mục chức năng; bài học: mở thư mục Windows thay cho xem ảnh NG, watchdog không ai đọc, nhúng ToolBlock editor đầy đủ |
| RefVis-B (sách hướng dẫn phần mềm vision thương mại) | Ba vùng cố định giống nhau trên mọi phần mềm của hãng; AutoRun/Setup; chú giải màu theo lỗi; sơ đồ vị trí; teach theo bước; Test Run; DB Sheet; giới hạn dừng; ưu tiên lỗi; lịch sử lọc; gói chẩn đoán |
| RefVis-C (khung vision nhà tích hợp) | Bảng lệnh; 5 vai trò + tự đăng xuất; công cụ chỉnh PMAlign trên ảnh thật, "chạy tất cả" thấy OK/NG ngay; `.vpp` gốc chỉ đọc |
| Application Wizard của VisionPro (tài liệu cài trên máy) | Nút Chạy liên tục/Một lần/Live/Cấu hình/Lưu; 3 mức đăng nhập; **dải 16 kết quả gần nhất**, bấm xem lại, lọc chỉ lỗi |
| GitHub: RefVis-N (VisionPro, WinForms) | Phân quyền 3 mức theo chức năng; log lọc mức/từ khoá; debug một ảnh / phát tuần tự thư mục; menu camera sinh từ cấu hình |
| GitHub: dự án VisionPro + camera Hikvision | Sửa ToolBlock tại chỗ bằng `CogToolBlockEditV2` — đối chiếu rủi ro |
| GitHub: RefVis-O | Luôn biết đang xem ảnh gốc hay ảnh trung gian; lý do OK/NG cạnh khoảng chấp nhận |
| GitHub: RefVis-P | Hiện điểm số, không chỉ "tìm thấy"; nhãn kết luận giữ nguyên khi phóng to |

## 2. Khung chung (kế thừa AM.AutoFrame v3, đổi phần lệnh cho phần mềm vision)

```
[1] Header+Nav 56   logo + tên máy · chip ONLINE/OFFLINE · chip trạng thái │ tab theo vai trò │ sản phẩm · đồng hồ+nhịp tim · ngôn ngữ · người dùng
[2] Banner 36→52    1 cảnh báo ưu tiên cao nhất + ACK + "+N"  HOẶC  lời nhắc chờ người xử lý (NG liên tiếp vượt giới hạn)
[3] Nội dung        theo màn
[4] Thanh lệnh 76   ONLINE/OFFLINE (nhấn giữ 1 s) · Đặt lại bộ đếm ca │ Chụp thử · Live (chỉ Offline) · chip "Thiết bị n/m · Máy · MES" + popup
```

Khác AM.AutoFrame: MeoVision không điều khiển máy (không Init/Start/Stop) — lệnh chính là **nhận lệnh từ
máy hay không** (Online/Offline). Offline: máy gửi lệnh sẽ nhận lỗi "vision không sẵn sàng", không treo.

## 3. Danh mục màn hình

| Màn | Level | Vai trò thấy tab | Chức năng chính | Chương sách |
|---|---|---|---|---|
| Vận hành | 1 | Mọi vai trò | Lưới camera theo `machine.json`, dải 16 kết quả/camera, kết quả gần nhất với từng mục kiểm (đo · khoảng), KPI ca, lỗi hàng đầu, nhật ký; sub-tab Sơ đồ vị trí, Bảng kết quả | 22 |
| Lịch sử & ảnh | 2 | Mọi vai trò | Lọc ngày/sản phẩm/lệnh/kết quả/lý do/serial; ảnh gốc + overlay; chạy lại với job hiện tại; đánh dấu NG giả; gói chẩn đoán | 22, 23 |
| Thống kê | 2 | Mọi vai trò | Xu hướng chỉ số + trung bình trượt + vùng cảnh báo sớm; OK theo giờ; Pareto lỗi đại diện; thời gian xử lý so ngân sách; trạng thái giới hạn dừng | 22 (dùng Ch16 §16.4) |
| Sản phẩm | 2 | Trưởng ca+ | Danh sách thư mục sản phẩm, vai trò cần/có; bảng lệnh; ngưỡng ngoài job; phiên bản + khôi phục; kích hoạt | 19 |
| Thiết bị | 3 | Trưởng ca+ | Trạng thái từng vai trò (Connected/Faulted, lý do, lần thử); thử camera/đèn; bản tin thô với máy | 18, 21 |
| Thiết lập ảnh | 3 | Kỹ sư+ | Chọn lệnh, nguồn ảnh (Live/chụp/ảnh mẫu); teach 6 bước; hộp công cụ ảnh; mục kiểm & dung sai; chạy thử bộ ảnh có nhãn; ToolBlock editor gốc trên bản sao | 20, 22 |
| Hiệu chuẩn | 3 | Kỹ sư+ | Hiệu chuẩn theo camera (ngày, RMS, hạn); wizard 5 bước do máy dẫn; nhánh tự động/chỉnh tay; "đã thay thiết bị"; vị trí chuẩn | 21 (Ch7 §7.7) |
| Hệ thống | 3–4 | Quản trị | Quyền + tài khoản + tự đăng xuất; lưu ảnh & ổ đĩa; giới hạn dừng + ưu tiên lỗi; sao lưu; ngôn ngữ/UiScale/kiosk/bố cục camera; chẩn đoán + log | 23, 24 |

## 4. Quyền

| Chức năng | Vận hành | Trưởng ca | Kỹ sư | Quản trị |
|---|---|---|---|---|
| Xem Vận hành / Lịch sử / Thống kê; ACK; gói chẩn đoán | ✓ | ✓ | ✓ | ✓ |
| Đặt lại bộ đếm ca; cho chạy tiếp sau NG liên tiếp; kích hoạt sản phẩm; kết nối lại thiết bị; Online↔Offline | — | ✓ | ✓ | ✓ |
| Teach; sửa bảng lệnh/dung sai; hiệu chuẩn; ToolBlock editor | — | — | ✓ | ✓ |
| Người dùng; lưu ảnh; giới hạn dừng; sao lưu | — | — | — | ✓ |

## 5. Quy tắc tương tác (áp cho mọi màn)

1. Màu chỉ cho OK/NG/cảnh báo/lỗi; thiết bị bình thường = chấm nhỏ.
2. Nút không dùng được: mờ, bấm vào hiện lý do (thiếu quyền / đang Online). Chỉ ẩn nguyên tab theo vai trò.
3. Online khoá mọi thay đổi cấu hình, chụp thử, live, teach. Chuyển Offline: nhấn giữ 1 s.
4. Vùng trống luôn nói bước tiếp theo.
5. Ảnh luôn kèm nguồn (camera / file / đang xem lại) và bước xử lý đang hiển thị.
6. Kết quả NG luôn kèm mục kiểm, giá trị đo, khoảng chấp nhận.
7. Mọi thay đổi ngưỡng, bảng lệnh, job, hiệu chuẩn: tự sao lưu bản cũ + ghi ai/lúc nào/lý do.
8. Chuỗi giao diện qua `ILocalizer`; mã lệnh, vai trò, tên file giữ nguyên, không dịch.
9. Kích thước chạm: lệnh chính 64 px, nút thường ≥ 48 px, dòng bảng ≥ 40 px.

## 6. Chưa chốt (hỏi chủ dự án)

- Công nghệ: giữ WinForms hay chuyển WPF (DESIGN_LOG [14] A).
- Vision PC có màn riêng hay dùng chung màn với phần mềm máy (ảnh hưởng nút chuyển màn, kích thước).
- Vận hành có được chuyển Online/Offline không (mockup: Trưởng ca+).
