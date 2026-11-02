# MeoVision — Phân tích UX theo tình huống sử dụng (v2.2, 2026-10-25)

Bản v2.1 (`UI_Spec_v2.md`) trả lời câu hỏi "phần mềm vision thật có những form gì". Bản này hỏi câu khác:
**làm một việc cụ thể thì phải mở những gì cùng lúc**, chuyển qua lại bao nhiêu lần, có phải dừng máy không.
Kết quả dùng để sửa mockup (`meovision_ui_mockup_v2.html`, bản 2.2) và ghi quyết định vào DESIGN_LOG [20].

## 1. Nguồn

| Nguồn | Cách lấy | Rút ra về luồng làm việc |
|---|---|---|
| RefVis-A, RefVis-C (chạy thật, Session 30) | Ảnh màn chính + form, đọc mã/cấu hình | Đèn và phơi sáng nằm trong form của **chức năng** (lệnh), không trong form camera; dock "Điều khiển tay" chọn chức năng + nguồn ảnh camera/thư mục/file + có trả lời client hay không; sửa cấu hình ở form Cài đặt dạng bảng, nút Hoàn tác/Áp dụng/Lưu |
| VisionMix (Halcon, WinForms) | Đọc mã | Mỗi tool có danh sách **ảnh thử** riêng (Tải ảnh / Xoá ảnh / Thử 1 lần / Thử liên tục); công cụ tìm mẫu có **tối ưu tham số trên bộ ảnh**: quét điểm tối thiểu × độ tham, báo tỷ lệ nhận dạng và thời gian trung bình, "lần chạy trước" so với "sau tối ưu"; nguồn ảnh của tool chọn camera hoặc **chế độ offline** (thư mục) + "Đặt làm ảnh mẫu"; màn chạy có Cài đặt camera, Liên tục/Huỷ, Thử/Huỷ, Đặt lại bộ đếm; form camera: phơi sáng, gain, xoay ảnh, trễ/chống rung trigger, thử tín hiệu ra; cửa sổ ảnh có biểu đồ mức xám theo đường (trung bình, độ lệch, khoảng, đỉnh) |
| VisionEdit (Halcon, kiểu nền tảng dòng chảy) | Đọc mã | Ảnh mẫu của tool tìm mẫu lấy từ "tải file" hoặc "ảnh của luồng hiện tại"; Tạo mẫu / Sửa mẫu; nguồn ảnh một ảnh / nhiều ảnh với Ảnh trước / Ảnh sau; Chạy một lần / Chạy liên tục; đo hai điểm trên ảnh; khoá bố cục cửa sổ; lưu ảnh gốc / lưu ảnh cửa sổ |
| VisionPlatform.Wpf (nhiều thư viện vision) | Đọc XAML | Màn **gỡ lỗi kịch bản**: tham số vào (tên, mô tả, giá trị, kiểu) + tham số ra + trạng thái chạy, thời gian thuật toán và tổng, nút "Chạy với ảnh", "Chạy", "Lưu kịch bản" trên cùng một màn; camera: Chụp 1 / Chụp liên tục / Dừng; hiệu chuẩn: lấy điểm vision + điểm máy qua giao diện có sẵn |
| VisionPro Tool (WPF, VisionPro) | Đọc XAML | Trang Chạy: ảnh vào từ file hoặc thư mục, Ảnh trước / Ảnh sau, chạy liên tục theo chu kỳ ms, lưu ảnh khi chạy xong, thống kê thời gian ToolBlock, bảng output |
| Battery tab inspection (WPF, OpenCV/Halcon) | Đọc mã | Tham số thuật toán đặt **cạnh** nút chạy và ảnh; nạp mẫu theo loại (OK / khuyết / lệch); hồ sơ sản phẩm có Huỷ sửa / Lưu; **mỗi bản ghi lịch sử lưu ảnh chụp tham số (JSON) lúc kiểm** để tái hiện trạng thái |
| IPC vision controller (WinForms .NET 8) | Đọc README | "Kích một lần" để thử máy; phân biệt **hàng lỗi** (loại, chạy tiếp) với **thiết bị lỗi** (dừng); chế độ mô phỏng ghi rõ trên thanh tiêu đề; ba tầng thời hạn theo thứ tự |
| Daheng camera GUI (PyQt) | Đọc mã | Thanh trượt + ô số cho phơi sáng theo **thang log**; Live bật/tắt; cân bằng trắng tự động **một lần**; Chụp vào thư mục |
| SimpleVision40, OpenHalcon | Đọc mã | Đọc ảnh có Ảnh trước / Ảnh sau, đếm số ảnh; cửa sổ ảnh có chế độ phóng/kéo/khoá |
| Cognex In-Sight EasyBuilder (tài liệu chính thức) | docs.cognex.com | Các bước ứng dụng theo thứ tự: Kết nối → **Thiết lập ảnh** (lấy ảnh, chỉnh thu ảnh, hiệu chuẩn) → Định vị → Kiểm tra → I/O → Giao tiếp → Lưu → Chạy job; quay lại bước bất kỳ được. **Filmstrip**: ghi ảnh khi chạy, tự chia thư mục Good/Bad, phát lại cho job chạy trên ảnh đã ghi; cửa sổ Filmstrip chỉ dùng khi offline |
| MVTec MERLIC (tài liệu chính thức) | mvtec.com | Frontend cho người vận hành: chạy, xem kết quả, **chỉnh các tham số đã định trước**; phân quyền nhóm (vận hành chỉ xem, người chỉnh máy được dạy lại và chỉnh); khi bật quyền ghi, trạng thái chuyển sang thủ công và **ngắt giao tiếp với máy** |
| Basler pylon Viewer (tài liệu chính thức) | docs.baslerweb.com | Lưu/nạp toàn bộ thông số camera ra file; "Tự chỉnh ảnh" là thao tác **một lần**, sau đó giá trị đứng yên; histogram có thể khác dữ liệu hàm tự động dùng (gamma) |

## 2. Mười tình huống sử dụng

Cột "v2.1 phải làm gì" đếm theo mockup v2.1.

| # | Tình huống | Ai · bao lâu một lần | Chức năng cần CÙNG LÚC | v2.1 phải làm gì | Vấn đề |
|---|---|---|---|---|---|
| S1 | Lắp máy mới: lấy nét, khẩu độ, căn camera | Kỹ sư · một lần mỗi máy, lại khi thay camera | Live, chỉ số độ nét, histogram, vùng cháy sáng, lưới căn tâm, phơi sáng/gain, đèn | Camera › tab Camera (editor VisionPro) rồi tab Đèn; không có độ nét, histogram | Chỉnh đèn ở tab khác thì mất ảnh live; không có số để biết "nét nhất chưa" |
| S2 | Tạo lệnh mới cho sản phẩm mới | Kỹ sư · vài lần một tháng | Ảnh (camera/file) · đèn + phơi sáng của **lệnh này** · job · output + giới hạn · bộ ảnh thử | Camera (chỉnh) → "Ghi vào T3" → Lệnh & job tab Job → tab Kết quả → quay lại Camera khi ảnh chưa đạt | Ba form, ít nhất 6 lần chuyển; đèn/phơi sáng gắn theo camera chứ không theo lệnh; giới hạn đặt không thấy số đo thật |
| S3 | Thử các mức đèn / phơi sáng để chọn | Kỹ sư · mỗi lệnh mới, khi đổi vật liệu | Live + điểm của job trên ảnh live; so 2–9 ảnh khác thông số cạnh nhau; chỉ số (xám trung bình, % cháy sáng, độ nét, điểm job) | Kéo từng giá trị, nhớ trong đầu ảnh nào đẹp hơn | Không so sánh được; không biết job còn đạt không ở thông số mới |
| S4 | Đầu ca / đổi lô | Vận hành · mỗi ca | Sản phẩm đang chạy, bộ đếm, kiểm mẫu chuẩn | Đặt lại bộ đếm; không có kiểm mẫu chuẩn | Không có cách chứng minh đầu ca "máy nhìn còn đúng" |
| S5 | NG giả tăng giữa ca: chỉnh ngưỡng | Kỹ sư · hằng tuần | Ảnh NG gần nhất · job trên ảnh đó · tham số · chạy lại trên N ảnh OK + N ảnh NG gần nhất · áp dụng | Phải **OFFLINE** (dừng máy) mới sửa; mở Lịch sử tìm ảnh → Lệnh & job → tab Job → "Chạy bộ ảnh" | Dừng máy cả lúc chỉ ngồi nghĩ; không có đường từ ảnh NG sang chỗ sửa; bộ ảnh thử từ đâu ra không rõ |
| S6 | Dạy lại mẫu (đổi lô linh kiện, đổi nhà cung cấp) | Kỹ sư · hằng tháng | Ảnh mới · vùng dạy · điểm trên bộ ảnh cũ và mới · lưu | Editor ToolBlock đầy đủ | Muốn đổi một mẫu phải lội cả cây tool; dễ sửa nhầm chỗ khác |
| S7 | Hiệu chuẩn lại sau va chạm / thay camera | Kỹ sư · hiếm | Live · đèn · máy dẫn từng điểm · sai số · kiểm bằng vật mẫu | Form Hiệu chuẩn (đủ) | Thiếu bước **kiểm lại bằng vật đo đã biết kích thước** sau khi hiệu chuẩn |
| S8 | Khách hàng khiếu nại một serial | Kỹ sư / QA · thỉnh thoảng | Tìm theo serial · ảnh · giá trị · **phiên bản job và tham số lúc đó** | Lịch sử có ảnh + giá trị | Không biết lúc đó chạy job/giới hạn nào (đã sửa bao nhiêu lần từ đó) |
| S9 | PLC báo không nhận được kết quả | Kỹ sư · thỉnh thoảng | Bản tin thô theo kênh · bit bắt tay · nhật ký lệnh | Mở Giao tiếp, chọn tab | Ô kênh ở thanh trạng thái không bấm được |
| S10 | Mất camera / đèn giữa ca | Vận hành báo, Kỹ sư xử lý | Lỗi cuối, số lần thử, nối lại, live kiểm tra | Mở Camera, tìm vai trò | Ô thiết bị ở thanh trạng thái không bấm được |

## 3. Chức năng thường đi cùng nhau

1. **Ảnh + đèn + phơi sáng + điểm job** là một cụm (S2, S3, S5). Ở RefVis-A, đèn (kênh, mức, trễ) và phơi sáng khai **trong chức năng**, không trong camera. Tách thành form Camera rồi "ghi vào lệnh" như v2.1 là đi ngược thói quen đó và gây chuyển qua lại.
2. **Tham số + chạy + bộ ảnh thử** là một cụm (S2, S5, S6). Bốn repo độc lập đều đặt tham số cạnh nút chạy và danh sách ảnh thử (VisionMix: mỗi tool một danh sách ảnh; Battery: mẫu theo loại; VisionPlatform: tham số vào/ra + Chạy + Lưu một màn; VisionPro Tool: Ảnh trước/Ảnh sau + thống kê). VisionMix còn chấm **trên cả bộ ảnh** (tỷ lệ nhận dạng, thời gian) và so "trước/sau".
3. **Nguồn ảnh là một lựa chọn chung** cho mọi việc chỉnh: camera live, chụp 1 ảnh, ảnh của lệnh gần nhất, ảnh NG trong lịch sử, thư mục. VisionMix gọi là "chế độ offline" của chính tool lấy ảnh; In-Sight có Filmstrip ghi khi chạy rồi phát lại.
4. **Giới hạn đặt khi nhìn thấy phân bố số đo thật** (S2, S5). Đặt "trên 0.5" mà không biết bộ ảnh OK đang cho 0.1–0.3 và NG cho 0.7–2.0 là đặt mò.
5. **Dừng máy để sửa** là chi phí lớn nhất (S5). In-Sight chỉ cho Filmstrip khi offline; MERLIC bật quyền ghi là ngắt giao tiếp. Cả hai đều không cho sửa job đang chạy — nhưng đều cho **chuẩn bị** trên ảnh đã ghi. Chỗ tốn thời gian là lúc chuẩn bị, không phải lúc áp dụng.
6. **Tham số định trước cho người không phải chuyên gia vision** (MERLIC Frontend): chỉ vài con số đã khai, có giới hạn, thay vì cả cây tool (S6).
7. **Truy vết cần ảnh chụp tham số** đi kèm kết quả (Battery lưu JSON tham số trong từng bản ghi) (S8).
8. **Đi từ chỗ thấy vấn đề tới chỗ sửa** bằng một cú bấm: khung ảnh → lệnh đó; ô kết quả → ảnh đó; dòng NG → lịch sử; ô thiết bị/kênh → form tương ứng (S5, S9, S10).

## 4. Quyết định v2.2

### D1. Một không gian "Thiết lập lệnh" thay cho form "Lệnh & job" + phần lệnh của form Camera

| Phương án | Đánh đổi |
|---|---|
| Giữ form tách rời (v2.1) | Quen kiểu RefVis-C; nhưng S2/S3/S5 chuyển form liên tục, đèn gắn sai chỗ |
| Trình hướng dẫn tuyến tính (bước 1 → 5, phải đi lần lượt) | Dễ cho người mới; nhưng sửa một giới hạn cũng phải qua 4 bước |
| **Không gian theo lệnh, các bước là tab đi được theo thứ tự bất kỳ ✅** | Như In-Sight EasyBuilder (bước có thứ tự, quay lại tự do). Một cửa sổ: trái = danh sách lệnh; giữa = ảnh + kết quả job; phải = bước đang chọn (Ảnh & đèn · Job · Kết quả & giới hạn · Bộ ảnh thử); đáy = bản nháp + Áp dụng. Form Camera lùi về **cấp máy** (lấy nét, kết nối, mặc định) |

### D2. Chỉnh khi đang ONLINE — ĐÃ CHỐT (tác giả, 2026-10-25): cho sửa, kèm chạy thử trên sản phẩm thật

Tác giả: dây chuyền có nhiều thay đổi nhỏ cần kiểm tra trên sản phẩm thật trong lúc đang chạy, không thể dừng máy rồi
làm từng việc. Phương án chọn = B + **chạy kèm** + **quay lại một chạm** (bảng dưới và mục D2b).

| Phương án | Đánh đổi |
|---|---|
| A. Khoá mọi chỉnh sửa khi ONLINE (v2.1; In-Sight, MERLIC) | An toàn tuyệt đối; nhưng máy dừng suốt lúc kỹ sư thử và suy nghĩ (S5) |
| **B. Bản nháp khi ONLINE, áp dụng nguyên khối giữa hai lệnh ✅ (cần tác giả chốt)** | Kỹ sư sửa trên **bản sao**, thử trên ảnh đã ghi (ảnh lệnh gần nhất, lịch sử, thư mục) trong khi máy vẫn chạy bản cũ. "Áp dụng" = lưu (có sao lưu) + nạp lại sản phẩm; bộ máy chạy lệnh của Chương 20 đã đổi sản phẩm **giữa hai lệnh** (lệnh đang chạy dùng job cũ tới khi xong — `LoadedProduct` + lease). Không cần OFFLINE. Camera live/chụp thử vẫn chỉ khi OFFLINE (không tranh camera với lệnh máy) |
| C. Sửa thẳng job đang chạy | Nhanh nhất; nhưng lệnh có thể chạy trên job sửa dở — đúng loại lỗi im lặng Chương 20 đã đo |

### D2b. Chạy kèm và quay lại (chốt)

- **Chạy kèm bản nháp trên sản phẩm thật:** mỗi lệnh thật vẫn được trả lời bằng bản đang chạy; **sau** khi trả lời,
  cùng ảnh đó chạy thêm trên bản nháp (một đối tượng job riêng, mở từ file — không đụng job đang trả lời máy). Màn hình
  đếm giống/khác, chênh thời gian, và giữ lại ảnh khác biệt để đưa vào bộ ảnh thử. Chạy kèm không được làm chậm câu trả
  lời: lệnh kế tiếp tới thì bỏ lượt chạy kèm (đếm số lượt bỏ).
- **Áp dụng** không bắt buộc chạy bộ ảnh thử (thay đổi nhỏ phải nhanh): hiện tóm tắt khác biệt + kết quả chạy kèm/bộ ảnh
  thử nếu có, Kỹ sư xác nhận; tự ghi ai, lúc nào, khác biệt; lý do tuỳ chọn.
- **Quay lại bản trước** một chạm: nạp lại bản đã áp dụng trước đó từ sao lưu (Ch19), cũng giữa hai lệnh.
- Khác phần mềm tham khảo: RefVis-A nhúng editor ToolBlock đầy đủ ngay trong app sản xuất và lưu đè, không sao lưu;
  MeoVision không bao giờ sửa đối tượng đang trả lời máy, và mọi lần áp dụng đều quay lại được.

### D3. Thử đèn và phơi sáng

- Trong cùng một khung live: histogram, tô màu vùng cháy sáng (255) và vùng đen (0), chỉ số **xám trung bình · % cháy sáng · độ nét · điểm job**. Điểm job lấy bằng cách chạy job của lệnh trên ảnh live (khi OFFLINE) — biết ngay thông số mới còn đạt không.
- **Chụp loạt**: chọn 3–5 mức phơi sáng × 1–3 mức đèn → lưới ảnh nhỏ, mỗi ô ghi chỉ số + điểm job; bấm một ô để lấy thông số đó vào bản nháp.
- **Tự chỉnh một lần** về xám trung bình đích (như pylon: một lần rồi đứng yên). Không có phơi sáng tự động liên tục khi chạy — ảnh phải giống nhau giữa các lệnh.
- Thanh trượt phơi sáng theo **thang log** (0,05–50 ms) + ô số.
- Nơi lưu rõ ràng: giá trị của lệnh → `product.json` (sản phẩm); mặc định camera → `machine.json` (máy). Màn hình ghi rõ đang sửa cái nào.

### D4. Bộ ảnh thử theo lệnh

- Nguồn: **ảnh gần nhất** = vài ảnh trong RAM (mặc định 8/lệnh, chỉnh được, để có ảnh OK khi chính sách lưu là "chỉ NG")
  + ảnh đã lưu trên đĩa đọc lại từ thư mục ảnh (tác giả chốt 2026-10-25: không giữ nhiều trong RAM); ảnh NG trong lịch sử; thư mục.
- Mỗi ảnh có nhãn **OK / NG / Bỏ qua**; lưu trong thư mục sản phẩm `samples\<lệnh>\{ok,ng}` (theo kiểu Good/Bad của Filmstrip), đi cùng sản phẩm khi chép sang máy khác.
- Chạy bộ ảnh cho bảng: nhãn · kết quả bản đang chạy · kết quả bản nháp · **đổi?** · giá trị; tổng tỷ lệ đúng nhãn, thời gian trung bình/lớn nhất (theo VisionMix).

### D5. Tham số nhanh

Mỗi lệnh khai trong `product.json` vài **tham số nhanh** = các terminal **input** của ToolBlock (ví dụ `ScoreMin`, `MaxScratchArea`) kèm tên hiển thị, đơn vị, min/max/bước. Bước "Job" hiện chúng thành thanh trượt; đổi là chạy lại ảnh đang xem; editor ToolBlock đầy đủ vẫn có (nút riêng) cho thay đổi lớn. Hợp đồng qua terminal input — không lội vào thuộc tính bên trong tool — nên bộ nạp kiểm được (terminal có thật, đúng kiểu, giá trị trong khoảng) như với output ở Chương 19.

### D6. Giới hạn đặt trên phân bố thật

Bước "Kết quả & giới hạn" vẽ cho mỗi output một dải phân bố giá trị trên bộ ảnh thử (OK xanh, NG đỏ) với hai vạch giới hạn kéo được, và đếm: bao nhiêu ảnh OK sẽ thành NG, bao nhiêu NG lọt.

### D7. Đường tắt theo ngữ cảnh

Bấm đầu khung ảnh → Thiết lập lệnh của lệnh đó, ảnh đang hiện làm ảnh làm việc (Kỹ sư). Bấm ô trong dải 16 kết quả → ảnh đó lên khung (mọi vai trò). Bấm dòng NG trong nhật ký → Lịch sử tại ảnh đó. Bấm ô thiết bị ở thanh trạng thái → Camera & đèn tại vai trò đó. Bấm ô kênh → Giao tiếp, tab Thử gửi/nhận, lọc theo kênh.

### D8. Kiểm mẫu chuẩn đầu ca — BỎ (tác giả, 2026-10-25)

Làm cho hàng chục máy mỗi ca là không cần khi sản xuất ổn định. Không đưa vào giao diện. Đề xuất cũ để đối chiếu:

Sản phẩm có thể khai **mẫu chuẩn**: vật thật đặt vào máy, giá trị mong đợi ± dung sai cho từng lệnh. Chạy bằng lệnh hệ thống từ máy (ví dụ đầu ca) hoặc Kỹ sư bấm; kết quả hiện ở dock và ghi nhật ký; không đổi cấu hình. Vận hành xem kết quả, không bấm được khi máy ONLINE (camera đang thuộc về lệnh máy).

### D9. Truy vết phiên bản

Mỗi kết quả ghi tên bản sao lưu sản phẩm đang chạy (bản sao lưu theo mã băm của Chương 19 đã có tên). Lịch sử hiện "chạy với bản 2026-10-25 09:14 · Kỹ sư · lý do ..." và nút xem khác biệt so với bản hiện tại.

### D10. Camera & đèn ở cấp máy

Form Camera chỉ còn việc của máy: kết nối, lấy nét/khẩu độ (độ nét + giá trị tốt nhất từng đạt, lưới căn tâm), mặc định phơi sáng, thử đèn — live và đèn **trên cùng một màn** (không tách tab). Editor VisionPro (`CogAcqFifoEditV2`) là tab thứ hai cho thông số sâu.

## 5. Đã chốt (2026-10-25)

1. D2: **cho sửa khi ONLINE** — bản nháp, chạy kèm trên sản phẩm thật, áp dụng giữa hai lệnh, quay lại một chạm.
2. D8: **bỏ** kiểm mẫu chuẩn đầu ca.
3. Ảnh: giữ ít trong RAM (mặc định 8 ảnh/lệnh), còn lại đọc từ ảnh đã lưu.
