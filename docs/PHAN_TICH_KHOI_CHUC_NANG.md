# Phân tích khối chức năng các chương trình vision tham khảo — đánh giá theo góc nhìn máy công nghiệp

> Lập 2026-10-31 (DESIGN_LOG [28]). Nguồn: đọc mã nguồn RefVis-A, C, D, E, F, G, H (bí danh — bảng đối chiếu ở thư mục
> private của dự án sách) + hai bản review cũ của RefVis-A/C. Số liệu "đếm mẫu" dưới đây đo bằng script trên toàn bộ
> file `.cs` (trừ Designer), không ước lượng.
>
> Mục đích: (1) biết MeoVision còn thiếu khối nào so với chương trình thật; (2) với những cách làm **nhiều chương
> trình cùng làm** mà thoạt nhìn là "không tốt", xét lại theo ràng buộc thật của máy công nghiệp — cái gì thật sự sai,
> cái gì là đánh đổi hợp lý — rồi đề xuất cách MeoVision nên làm.

## 1. Bản đồ khối chức năng

✅ có và đáng học · ◐ có nhưng nửa vời/rủi ro · ✗ không có · — không áp dụng

| Khối | A | C | D | E | G | H | MeoVision |
|---|---|---|---|---|---|---|---|
| Khởi động có kiểm (license, thư mục, thiết bị) | ◐ | ◐ license | ◐ | ✗ | ✗ | ◐ | ✅ chuỗi bước, then chốt |
| Camera nhiều hãng qua lớp trừu tượng | ✗ | ✅ ~12 hãng | ◐ 3 hãng | ✗ | ✗ | ◐ | ◐ GigE Cognex + thư mục (đủ cho sách) |
| Giám sát kết nối + tự nối lại | ◐ ping, không chặn gì | ? | ◐ mở lại 3 lần | ✗ | ✗ | ◐ trạng thái | ✅ watchdog thiết bị + kênh |
| Đèn: kênh, cường độ, chờ ổn định | ✅ | ✅ 5 hãng | ◐ | ✅ | ✅ | ✅ | ✅ (thêm `settleMs` S24) |
| Sản phẩm/recipe = thư mục tự chứa | ✅ | ✅ | ✅ | ✗ 1 bộ | ✅ | ✅ | ✅ + kiểm toàn bộ khi nạp |
| Máy chọn sản phẩm (lệnh đổi / mã trong trigger) | ✅ mã trong trigger | ✅ SW | — | ✗ | ✗ tay | ✅ PLC gửi loại xe | ✅ SW + mã sản phẩm trong lệnh |
| Bảng lệnh (mỗi lệnh: camera, đèn, job, output) | ✅ | ✅ + lệnh ghép/bước | ◐ bước cứng trong code | ✗ code cứng | ✗ code cứng | ◐ | ✅ `product.json` |
| Trình tự có trạng thái tường minh | ✗ mảng bước + busy-wait | ? | ✅ station | ✗ vòng lặp UI | ✗ vòng đọc IO | ✅ state machine | ✅ engine theo lệnh |
| Kết quả 3 trạng thái OK / NG / LỖI tới máy | ◐ chuỗi | ◐ 999 | ◐ 999 | ◐ | ✗ chỉ báo NG | ✅ 3 bit | ✅ + lý do |
| Báo máy "vision đang ở bước nào / còn sống" | ✗ | ? | ✗ | ✗ | ✗ | ✅ word trạng thái từng bước | ◐ Online + Heartbeat + Ack/Complete |
| Mã lỗi tập trung + gợi ý xử lý | ✗ | ✗ | ✅ mã + bảng CSV | ✗ | ✗ | ◐ câu "kiểm tra camera số i" | ◐ khoá ngôn ngữ, chưa có "cách xử lý" |
| Bỏ qua vision (bypass / tắt kiểm) | ? | ? | ✅ trả OK sau 200 ms | ✅ chạy rỗng | ✅ cờ "dùng kiểm" | ✅ chế độ ByPass | ✗ |
| Hiệu chuẩn có giao thức với máy | ✅ C,i,x,y | ✅ CC/CS/CN/CE… | ◐ | ✗ | ✗ | ✗ | ✅ CS/CP/CE (form chưa có) |
| Thiết lập/teach có bản nháp, quay lại | ✗ | ✗ | ◐ bản "lần trước" | ✗ | ✗ | ✗ | ✅ bản nháp + chạy kèm + áp dụng/quay lại |
| Lưu ảnh gốc / ảnh đồ hoạ / theo NG-OK | ✅ | ✅ gốc+cửa sổ × All/OK/NG | ✅ 0/1/2 | ✅ BMP+JPG riêng ngày | ✅ OK/NG/NG+UI | ✅ | ✅ (đồ hoạ thêm S23) |
| Dọn đĩa theo ngày + theo dung lượng | ✅ ngày | ✅ ngày + % đĩa | ✅ | ✅ ngày riêng loại | ◐ ngày | ✅ % đĩa | ✅ ngày + GB |
| Kết quả ra file / CSDL / MES | ◐ CSV, MES chết | ✅ dữ liệu theo lệnh | ✅ CSDL | ✅ MES + lấy mẫu | ◐ | ✅ SQL Server | ◐ CSV (MES chưa) |
| Thống kê theo ca | ✗ | ✗ | ◐ | ✅ ca ngày/đêm | ✗ | ✅ ca | ◐ bộ đếm tổng |
| Người dùng, quyền, tự đăng xuất | ◐ mật khẩu chữ rõ | ✅ 5 vai trò + idle | ✅ quyền theo tham số | ◐ 3 mật khẩu chữ rõ | ✗ | ◐ | ✅ 3 vai trò, PBKDF2, nhật ký |
| Màn giao tiếp để thử (gửi/nhận tay) | ◐ | ✅ cây server, gửi lặp | ✅ | ✗ | ✗ | ✅ form PLC | ✗ |
| Điều khiển tay / nguồn ảnh camera-thư mục-file | ◐ | ✅ tab Điều khiển tay | ✅ debug vision | ✗ | ◐ | ✅ test | ◐ (trong Thiết lập lệnh) |
| Sao lưu trạm, chẩn đoán, kiểm cài đặt | ✗ | ✗ | ◐ bản Bak | ✗ | ✗ | ✗ | ✅ |

**Đọc bảng:** MeoVision đã có đủ khối lõi và hơn các chương trình thật ở *kiểm khi nạp*, *bản nháp*, *ba trạng
thái + lý do*, *sao lưu/chẩn đoán*. Các khối chương trình thật có mà MeoVision **chưa có**: (1) bypass có kiểm soát,
(2) màn giao tiếp để thử, (3) mã lỗi kèm cách xử lý, (4) form hiệu chuẩn, (5) thống kê theo ca, (6) MES, (7) báo máy
bước hiện tại chi tiết hơn.

## 2. Những cách làm nhiều chương trình cùng làm — đúng hay sai trên máy thật?

Đếm mẫu trong mã nguồn (file `.cs`, trừ Designer):

| Mẫu | D (94 nghìn dòng) | E (6,7 nghìn) | F (36 nghìn) | G (18 nghìn) |
|---|---|---|---|---|
| `Thread.Sleep` | 137 | 64 | 58 | 10 |
| `catch {}` rỗng | 62 | 2 | 56 | 1 |
| `MessageBox.Show` | 239 | 34 | 107 | 27 |
| `CheckForIllegalCrossThreadCalls = false` | 0 | 3 | 2 | 1 |
| `while (true)` | 22 | 4 | 12 | 1 |

### 2.1 Chờ bằng vòng lặp + `Thread.Sleep` (thăm dò) — **cách làm đúng, cách viết sai**

- *Thấy:* G đọc 32 đầu vào card IO mỗi 1 ms; H mỗi 50 ms một bước; E chờ phản hồi xi lanh bằng vòng gửi-lại 50 ms.
- *Góc máy công nghiệp:* PLC tự nó là vòng quét; MC 3E, Modbus không có "sự kiện" — **thăm dò theo chu kỳ là cách
  chuẩn**, dự đoán được, PLC lập trình viên hiểu ngay. Cái sai không phải thăm dò mà là ba chỗ đi kèm:
  1. **Không có thời hạn** — E gửi lại lệnh xi lanh *mãi mãi* nếu bo không trả "Pass"; H chờ `IsGrabbing()` /
     `IsFinished()` không hạn → treo im lặng, máy đứng không ai biết vì sao.
  2. **Chờ trên luồng giao diện** — E chạy cả chu trình (kể cả chờ trục) trong `Invoke` → màn hình đơ mỗi chu kỳ.
  3. **Trễ cố định thay cho tín hiệu** — E chờ 1200/1500 ms "cho chắc". Hợp lý khi chờ thứ vật lý không có cảm biến
     (đèn ổn định, rung tắt), sai khi chờ thiết bị có trả lời.
- *MeoVision:* giữ thăm dò cho PLC (`PlcChannel` chu kỳ cấu hình được) — **đúng**. Mọi chờ đều có hạn (lệnh
  `timeoutMs`, mở thiết bị, kênh), không chờ trên luồng UI, trễ cố định chỉ dùng cho đèn (`settleMs`, có trần 2000 ms).
  **Không cần đổi.** Ghi thành quy tắc trong sách: *"thăm dò được, chờ không hạn thì không"*.

### 2.2 Chống nhiễu trigger bằng cửa sổ thời gian — **hợp lý cho đầu vào thô, sai làm cơ chế duy nhất**

- *Thấy:* G bỏ mọi thay đổi đầu vào trong vòng 1000 ms sau lần trước, rồi `Task.Run` chụp.
- *Góc máy:* tiếp điểm rơ-le, cảm biến dội có thật; lọc là cần. Nhưng cửa sổ 1 s **giới hạn nhịp máy ≤ 1 sản
  phẩm/giây/đầu vào** và nuốt trigger thật nếu PLC bắn nhanh; không có xác nhận ngược nên PLC không biết trigger có
  được nhận không.
- *MeoVision:* bắt tay Trigger → TriggerAck → InspectionComplete, PLC tắt Trigger → vision tắt bit (kiểu NDM) — một
  trigger chỉ chạy một lần, không phụ thuộc thời gian. **Đúng hơn, giữ.** Nếu sau này đọc trigger từ card IO số
  (không qua PLC) thì thêm lọc *ổn định N ms* (tín hiệu giữ mức N ms mới tính), không dùng cửa sổ chặn.

### 2.3 Nuốt lỗi (`catch {}`), ghi log rồi chạy tiếp — **đúng ý "máy không được dừng", sai khi biến lỗi thành OK**

- *Thấy:* D 62, F 56 khối catch rỗng; A: script hiệu chuẩn bắt exception chỉ vẽ nhãn đỏ → khối vẫn Accept → báo OK
  cho PLC (đã chứng minh bằng spike); G: chụp hỏng thì `return` — **không trả gì cho máy**; D: tắt vision thì *trả OK*.
- *Góc máy:* nhà máy ưu tiên **sẵn sàng** — một lỗi lẻ (một ảnh hụt, một lần ghi CSDL hỏng) không được dừng cả
  dây chuyền; nên "log rồi chạy tiếp" có lý. Nhưng có một ranh giới tuyệt đối: **không bao giờ để lỗi trở thành
  OK**, và **không bao giờ im lặng với máy**. Vision báo OK sai = hàng lỗi ra khách; vision im lặng = máy chờ hết hạn
  rồi mới biết, hoặc tệ hơn, PLC coi "không có NG" là OK (G chỉ bật bit khi NG).
- *MeoVision:* lỗi của một lệnh → trả **ERR + lý do** (`cmd.*` → mã ngắn), không bao giờ OK; lỗi phụ (lưu ảnh, ghi
  CSV) không chặn trả lời máy nhưng vào bộ đếm lỗi + log; job có script không biên dịch được bị bộ nạp chặn ngay
  khi nạp sản phẩm. **Đúng, giữ.** Script *biên dịch được nhưng tự nuốt exception* thì MeoVision không phát hiện —
  đó là kỷ luật viết script (Chương 12).
  Còn thiếu: *bộ đếm lỗi phụ hiện lên màn chính* (người ta biết ảnh không còn được lưu) — xem đề xuất D4.

### 2.4 `MessageBox` khi lỗi trong lúc chạy — **đúng ý "phải có người biết", sai cách**

- *Thấy:* E bật `MessageBox` khi tải MES lỗi *ngay trong chu trình* → chu trình dừng tới khi có người bấm; D 239 lần,
  H hộp lỗi + đèn tháp đỏ + dừng trình tự.
- *Góc máy:* lỗi cần người can thiệp phải **ồn ào** — đúng. Nhưng hộp thoại modal (1) chặn luồng chu trình nếu gọi từ
  đó, (2) không đến được PLC nên máy không biết tại sao vision đứng, (3) ca đêm không ai ngồi trước màn hình — sáng ra
  thấy 40 hộp thoại chồng nhau, (4) người vận hành học cách bấm OK mà không đọc.
- *MeoVision:* lỗi lệnh → ERR cho máy (máy quyết định dừng hay bỏ sản phẩm); trạng thái kênh/thiết bị → thanh trạng
  thái + nhật ký màu; hộp thoại chỉ cho thao tác *người dùng tự bấm*. **Đúng.** Còn thiếu một **dải cảnh báo
  không chặn** trên màn chính cho lỗi kéo dài (thiết bị mất, ổ ảnh đầy, file trạm hỏng) — đề xuất D3.

### 2.5 Tắt kiểm tra luồng giao diện (`CheckForIllegalCrossThreadCalls = false`) — **sai, không có lý do công nghiệp nào**

- *Thấy:* E, F, G — để luồng nền gán ảnh/chữ thẳng vào control.
- *Góc máy:* lý do thật là "làm cho chạy được nhanh". Hậu quả là lỗi **ngẫu nhiên, hiếm, không tái hiện được** (đơ
  màn, control vẽ sai, đôi khi sập) — đúng loại lỗi tốn nhiều ngày công hiện trường nhất.
- *MeoVision:* mọi cập nhật màn hình qua `BeginInvoke`; `DisplayFrame` đếm tham chiếu ảnh. **Đúng, giữ.**

### 2.6 Bỏ qua vision (bypass) — **cần thiết trên máy thật, nguy hiểm khi làm như các chương trình tham khảo**

- *Thấy:* D: tham số `VisionEnable = false` → hàm chụp **trả OK sau 200 ms**; H: chế độ ByPass; G: cờ "dùng kiểm";
  E: "chạy rỗng"; H còn *hướng dẫn* người vận hành đặt camera hỏng thành "không dùng" để chạy tiếp.
- *Góc máy:* camera hỏng lúc 2 giờ sáng, đơn hàng phải giao → dây chuyền **phải** chạy tiếp được với kiểm tay/kiểm
  sau; chạy rỗng khi chỉnh máy cũng cần. Không có bypass thì người ta tự chế (rút cáp, sửa PLC) — tệ hơn. Cái nguy
  hiểm là bypass **im lặng**: trả OK như thật, không ai nhớ bật lại, hàng không kiểm lẫn vào hàng đã kiểm.
- *Đề xuất cho MeoVision (D1):* bypass là một **chế độ** có tên, không phải cờ ẩn:
  - kết quả là **BYPASS** (mã trạng thái riêng, PLC phân biệt được với OK; vẫn chụp + lưu ảnh nếu camera còn);
  - chỉ Kỹ sư bật, theo từng lệnh, **bắt buộc nhập lý do**, ghi nhật ký;
  - **hết hạn tự động** (ví dụ cuối ca / N giờ) và **mất khi khởi động lại**;
  - dải vàng trên màn chính + bit/word riêng tới PLC + đếm riêng trong CSV ("không kiểm: 312 sản phẩm").

> **Tác giả chốt 2026-10-31 (DESIGN_LOG [29]):** bỏ qua chỉ áp cho lệnh **kiểm tra** (máy test/AOI, sản phẩm phải qua
> trạm). Lệnh **dẫn hướng** (vision trả X, Y, góc cho chuyển động) không bao giờ bỏ qua — `kind: Guide` trong
> `product.json`. Lý do NG/LỖI không gửi máy (trường lý do luôn trống), chỉ ghi log + CSV + màn vision.

### 2.7 Giá trị lính canh `999` cho "không đo được" — **quen thuộc với PLC, nhưng là dữ liệu giả**

- *Thấy:* A, C, D, E đều dùng `999`/`-999` thay cho giá trị khi lỗi.
- *Góc máy:* lập trình viên PLC quen, chương trình PLC cũ đã viết theo; nhưng `999` có thể là giá trị thật (đơn vị
  µm), và lọt vào thống kê/Cpk như số đo.
- *MeoVision:* trạng thái ERR + bit `ResultsValid`/`Error` riêng, giá trị để trống trong CSV. **Đúng.** Nếu gặp máy
  có chương trình PLC cũ bắt buộc `999`: thêm tuỳ chọn `errorValue` theo kênh (chỉ ở đầu ra tới PLC, không ghi vào
  CSV) — đề xuất D7, chỉ làm khi có máy cần.

### 2.8 Tắt timeout của tool (`TimeoutEnabled = false`) — **hợp lý**

- *Thấy:* A 30/30 PMAlign, C mọi tool, A còn tắt timeout cả AcqFifo.
- *Góc máy:* timeout *trong tool* làm kết quả phụ thuộc tốc độ máy (máy bận → tool dừng sớm → NG giả) — người ta tắt
  để kết quả lặp lại được. Hợp lý, **với điều kiện** có thời hạn ở tầng lệnh. A tắt luôn thời hạn chụp → treo.
- *MeoVision:* tool để nguyên như job; thời hạn ở tầng lệnh (`timeoutMs`) + chụp có thời hạn. **Đúng.**

### 2.9 Cấu hình INI sửa bằng Notepad, mật khẩu chữ rõ — **nửa đúng**

- *Góc máy:* kỹ thuật viên hiện trường không có công cụ gì ngoài Notepad → file văn bản sửa tay được là **yêu cầu
  thật**. Mật khẩu chữ rõ: mô hình đe doạ trong xưởng thấp (chính sách của dự án: ưu tiên sẵn sàng, không khoá tài
  khoản) — nhưng mật khẩu chữ rõ trong file mà ai cũng mở được thì quyền chỉ còn là trang trí.
- *MeoVision:* JSON sửa tay được + kiểm khi nạp + hai lớp (S24); mật khẩu PBKDF2, file khôi phục Quản trị ồn ào.
  **Đúng.**

### 2.10 Singleton toàn cục, form "ôm hết" — **chấp nhận được cho một máy, trả giá khi bảo trì**

- *Thấy:* D gọi `GetInstance()` 2738 lần; A, G form chính hơn 3000 dòng.
- *Góc máy:* phần mềm một máy, một nhóm viết — singleton là thực dụng, không phải "sai" về vận hành. Cái giá là không
  thử được nếu không có phần cứng, và sửa chỗ này vỡ chỗ khác.
- *MeoVision:* DI + interface để test không cần phần cứng (167 test). **Giữ**, không cần bàn thêm.

### 2.11 Lưu ảnh trên luồng chu trình / luồng giao diện, BMP — **sai khi nhịp máy chặt**

- *Thấy:* G lưu ảnh trong `BeginInvoke` (luồng UI); E lưu + tải ảnh trong `Invoke`; A lưu trên ThreadPool (tốt hơn).
- *MeoVision:* hàng đợi nền, giới hạn số ảnh chờ, đầy thì bỏ ảnh giữ dòng kết quả. **Đúng.** BMP: xem backlog S2.

### 2.12 Chạy lại / dừng luồng bằng `Thread.Abort`, khởi động lại để phục hồi — **sai**

- *Thấy:* H `Abort` luồng trình tự cũ khi bắt đầu lại; lỗi thì dừng hẳn trình tự, chờ người bấm chạy lại.
- *Góc máy:* "lỗi thì dừng chờ người" là **an toàn** cho máy có chuyển động — đúng tinh thần. Nhưng vision không điều
  khiển chuyển động: vision lỗi một lệnh không có lý do dừng nhận lệnh sau; để máy (PLC) quyết định.
- *MeoVision:* lỗi một lệnh → ERR, lệnh sau vẫn chạy; thiết bị hỏng → watchdog nối lại; không `Abort`. **Đúng.**

## 3. Đề xuất cho MeoVision (theo thứ tự nên làm)

| Mã | Việc | Học từ / vì sao | Công | Ưu tiên |
|---|---|---|---|---|
| D1 | **Chế độ bỏ qua có kiểm soát (BYPASS)** theo lệnh: kết quả mã riêng, lý do bắt buộc, Kỹ sư, tự hết hạn + mất khi khởi động lại, dải vàng, bit/word PLC, đếm riêng | §2.6 — mọi máy thật đều có; các bản tham khảo làm im lặng | 1–2 ngày | **P1** |
| D2 | **Màn Giao tiếp**: mỗi kênh trạng thái, lệnh vào/ra gần nhất (chuỗi thô + thời gian), gửi lệnh thử, xem bit/word PLC sống | C, D, H đều có; nghiệm thu với PLC không có màn này rất mù | 1–2 ngày | **P1** |
| D3 | **Dải cảnh báo không chặn** trên màn chính cho lỗi kéo dài (thiết bị mất, ổ ảnh đầy, file trạm hỏng, Data trong OneDrive, bypass đang bật) — bấm vào thấy chi tiết + cách xử lý | §2.4 — thay hộp thoại; H dùng đèn + hộp thoại | 1 ngày | **P1** |
| D4 | **Mã lỗi kèm "cách xử lý"**: mỗi khoá lỗi có câu gợi ý cho người vận hành (file ngôn ngữ, khoá `*.hint`) — hiện trong dải cảnh báo, nhật ký, Lịch sử | D có bảng mã lỗi người dùng sửa được; H ghi "kiểm tra camera số i" | 0,5–1 ngày | P1 |
| D5 | **Form Hiệu chuẩn**: xem điểm đã nhận, RMS, ảnh từng điểm, lưu/huỷ — giao thức CS/CP/CE đã có | A, C có form; hiện chỉ máy dẫn được | 2 ngày | P2 |
| D6 | **Word "bước hiện tại" tới PLC** (Chờ / Đèn / Chụp / Chạy job / Trả kết quả / Lỗi) | H — PLC thấy vision kẹt ở đâu | 0,5 ngày | P2 |
| D7 | Tuỳ chọn `errorValue` (vd 999) cho đầu ra tới PLC của kênh — chỉ khi máy cần | §2.7 | 0,5 ngày | P3 |
| D8 | Bộ đếm theo ca + thống kê nhịp (cycle time) theo ca | E, H | 1 ngày | P2 |
| D9 | Nguồn ảnh thủ công cho cả lệnh (camera / thư mục / file) để nghiệm thu không có hàng — "chạy rỗng" của E | C tab Điều khiển tay | 1 ngày | P2 |
| D10 | Kênh MES/CSDL (client, gọi rồi thôi — tách khỏi kênh giữ kết nối) | A (MES chết), E, H | 2–3 ngày | P3 |

Các mục backlog S1, S2, C1, T1, U1 (file `BACKLOG_TU_DU_AN_THAM_KHAO.md`) vẫn giữ; D1–D10 bổ sung.

## 4. Quy tắc rút ra (đưa vào sách khi viết lại Ch20–Ch24)

1. Thăm dò được; **chờ không hạn thì không**; không chờ trên luồng giao diện.
2. Lỗi không bao giờ thành OK; vision **không bao giờ im lặng** với máy — luôn OK / NG / ERR (+ BYPASS khi có).
3. Lỗi phụ (lưu ảnh, CSDL, MES) không chặn trả lời máy, nhưng phải **thấy được** trên màn chính.
4. Người cần biết → dải cảnh báo + bit tới PLC; hộp thoại chỉ cho thao tác người dùng tự bấm.
5. Bỏ qua kiểm tra là một **chế độ có tên, có hạn, có người chịu trách nhiệm**, không phải một cờ.
6. Trễ cố định chỉ cho thứ vật lý không đo được (đèn, rung), có trần và cấu hình được.
