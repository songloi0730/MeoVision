# ROADMAP_HOAN_THIEN — Lộ trình hoàn thiện MeoVision

> Phần mềm xử lý ảnh công nghiệp với Cognex VisionPro — lộ trình xây dựng theo
> **kiến trúc chuẩn 10 thành phần** tác giả đưa ra (Session 6).
>
> **Cách đọc:** file này là KẾ HOẠCH (cái gì, khi nào, tiêu chí xong). *Vì sao* chọn
> phương án nào nằm trong `design-notes/DESIGN_LOG.md` (quyết định mới → thêm mục `[N]`
> vào cuối file đó, không ghi lý do dài ở đây). Trạng thái tức thời → `PROJECT_STATUS.md`.
>
> **Cập nhật lần cuối:** 2026-07-25 (Session 6 — tạo file, chốt khung V4–V8; cùng ngày:
> V4 đã triển khai + kiểm chứng thật (DESIGN_LOG [6]), V5 viết lại thành "recipe đa bước +
> backup vpp/config" theo quyết định DESIGN_LOG [7] — bài học RefVis-A xem lần 2).

---

## §1. Kiến trúc chuẩn 10 thành phần — đối chiếu hiện trạng

Một phần mềm xử lý ảnh công nghiệp hoàn chỉnh (xét kiến trúc tổng thể, không riêng SDK)
gồm 10 thành phần. Bảng dưới đối chiếu từng thành phần với hiện trạng MeoVision sau V0–V3
và chỉ ra khoảng trống → giai đoạn xử lý:

| # | Thành phần | Hiện trạng MeoVision (V0–V3) | Khoảng trống | Giai đoạn |
|---|---|---|---|---|
| 1 | **Thu nhận ảnh** (driver camera GigE/USB3, trigger HW/SW/free-run, exposure/gain/ROI, điều khiển đèn) | ✅ `ICameraSource` + `CognexCameraSource` (GigE qua `CogFrameGrabberGigEs`/`ICogAcqFifo`, compile-verify) + `SimulatedCameraSource`; `CameraTriggerMode` 4 chế độ; strobe = tham số trên `VisionRecipe` | Exposure/Gain/ROI chưa là tham số recipe; chưa chạy `GrabAsync()` với camera vật lý; chưa có phát hiện mất kết nối chủ động | **V7** (giám sát), phần cứng chặn chạy thật |
| 2 | **Tiền xử lý / hiệu chỉnh** (lens distortion, pixel→mm, hand-eye) | ❌ Chưa có — `VisionResult.PartX/PartY` đang là toạ độ pixel | Calibration pixel→mm (checkerboard); hand-eye NGOÀI phạm vi (không có robot) | **V8** |
| 3 | **Thư viện công cụ vision** (locate/measure/identify/inspect) | ✅ `CognexVisionEngine` chạy `.vpp` (ToolBlock) qua `TerminalNames` chuẩn; `SimulatedVisionEngine` parity | `.vpp` thật chưa có (tác giả dựng bằng QuickBuild); Teach UI throw `NotSupportedException` có chủ đích | **V8** + việc tác giả |
| 4 | **Quản lý Job/Recipe** (recipe theo model, versioning, backup/restore) | 🟡 `IRecipeRepository` JSON (Newtonsoft) — load/save cơ bản; recipe cứng 1 ToolBlock | **Recipe đa bước** (1 phần mềm nhiều máy — DESIGN_LOG [7]); versioning schema, backup/restore vpp+config, đổi model an toàn, audit | **V5** |
| 5 | **Logic quyết định & SPC** (pass/fail, ngưỡng, control chart, Cpk) | 🟡 `InspectionCycleService` pass/fail + retry theo `FailureReason`; ngưỡng nằm trong recipe | Thống kê SPC: đếm OK/NG theo ca, yield, Cpk cơ bản, dữ liệu control chart | **V6** |
| 6 | **Lớp giao tiếp** (PLC Modbus/EtherNet-IP/IO, OPC UA, robot) | 🟡 `IPlcCommunication` 4 tín hiệu (Trigger/Busy/Done/Ack) trung lập phương tiện; `SimulatedPlcCommunication` chạy demo | Phương tiện thật: **TCP `127.0.0.1`** (đã chốt, DESIGN_LOG [6]) — `TcpPlcCommunication` + app test receiver riêng | **V4** (đang làm) |
| 7 | **HMI** (màn kỹ sư cấu hình / màn operator, overlay kết quả) | ✅ `FormMain` 3 tab (Vận hành/Lịch sử/Cấu hình), PictureBox+GDI+ overlay | Trạng thái kết nối camera/PLC trên UI; phân tách operator/engineer NGOÀI phạm vi V1 (code sách, giữ đơn giản) | **V7** (chip trạng thái) |
| 8 | **Lưu trữ & truy vết** (ảnh NG + OK theo tỷ lệ, log kèm timestamp/mã SP) | 🟡 `INgImageArchiver` (chỉ NG — policy ở cycle service); `IInspectionResultStore` in-memory | Store bền vững (SQLite), lưu ảnh OK theo tỷ lệ mẫu, retention xoá dữ liệu cũ | **V6** |
| 9 | **Runtime/Deployment** (bản chạy độc lập, license Runtime) | ❌ Chưa có — mới chạy từ máy dev | Gói xcopy-deploy + checklist license VisionPro Runtime + config theo máy | **V8** |
| 10 | **Chẩn đoán & watchdog** (mất camera, timeout, chạy 24/7) | 🟡 2 watchdog độc lập trong WorkStation.Demo (PLC rộng hơn `CycleTimeoutMs`); `IAlarmService` | Phát hiện mất kết nối camera/PLC chủ động, chính sách reconnect, soak test chạy dài | **V7** |

**Đọc bảng thế nào:** ✅ = đủ dùng cho mục tiêu dự án; 🟡 = có nền, cần bồi; ❌ = chưa có.
MeoVision là phần mềm minh hoạ cho sách nhưng ưu tiên "giống phần mềm thật" (DESIGN_LOG [4]) —
"đủ dùng" nghĩa là *một kỹ sư vision đọc code hiểu được cách làm thật*, không phải "đầy đủ
mọi tính năng của phần mềm thương mại".

---

## §2. Nguyên tắc bất biến (nhắc lại — chi tiết ở `CLAUDE.md`)

Mọi giai đoạn dưới đây PHẢI giữ nguyên:

1. **net48, 1 process, không IPC** (DESIGN_LOG [2], kiểm chứng runtime thật ở [5]).
2. **Chỉ `MeoVision.Engine` reference Cognex xử lý ảnh** (`CogToolBlock`, `ICogImage`...);
   project khác chỉ thấy `IVisionEngine`/`VisionResult`. UI được phép (tuỳ chọn) nhúng
   control *hiển thị* Cognex (`CogRecordDisplay`) — ranh giới làm rõ ở [4].
3. **Simulation parity** — tính năng mới trên interface Core phải có cả bản thật lẫn bản
   `Simulated` chạy được không cần license.
4. **Mọi API `Cog*` phải kiểm chứng bằng reflection trước khi viết** (52 XML IntelliSense
   trong `reference/sdk_api_xml/` của dự án sách; không thấy = API bịa).
5. **Sách cập nhật theo code** — code đổi chạm nội dung đã in → chạy skill
   `meovision-book-sync-check`, báo tác giả, KHÔNG tự sửa sách.
6. Không thêm tính năng "cho đủ" ngoài lộ trình — mỗi mục trong file này phải trỏ về một
   thành phần checklist §1 hoặc một yêu cầu tác giả đã nêu.

---

## §3. Lộ trình V4 → V8

Đánh số nối tiếp V0–V3 đã xong. Mỗi giai đoạn: **Mục tiêu → Phạm vi mã chi tiết →
Phương án (khi có nhiều cách) → Kiểm thử → DoD → Phụ thuộc**.

---

### V4 — Truyền thông PLC thật: TCP `127.0.0.1` + test receiver  *(checklist #6 — ĐÃ TRIỂN KHAI)*

> **Trạng thái (cập nhật cùng ngày, xem DESIGN_LOG [6] "Đã triển khai"):** code + kiểm chứng
> thật XONG — chạy đồng thời 2 process qua `127.0.0.1:5050`, tín hiệu TRIGGER/BUSY/DONE/ACK
> đúng thứ tự. **Khác kế hoạch dưới 3 điểm:** (1) `TcpPlcCommunication` đặt trong
> `WorkStation.Demo` chứ không phải `Application`; (2) `PlcTestReceiver` độc lập HOÀN TOÀN —
> không reference cả `MeoVision.Core` (công cụ kiểm tra khách quan); (3) chưa thêm
> `PlcCommunicationException` (phương án B dưới) — quyết định khi V7 cần phân nhánh reconnect.
> **Còn lại của V4:** kiểm thử tự động (unit framing + integration in-process) và DoD gạch 2
> (rút receiver giữa chừng) — làm nốt trước khi đóng V4.

**Mục tiêu:** thay `NotImplementedPlcCommunication` bằng phương tiện truyền thật đã chốt
(TCP loopback, DESIGN_LOG [6]); tác giả tự kiểm tra được tín hiệu bằng app nhận riêng.

**Phạm vi mã:**

| Hạng mục | Yêu cầu chi tiết |
|---|---|
| `TcpPlcCommunication : IPlcCommunication` | Đặt trong `MeoVision.Application` (không đụng Cognex). `TcpClient` net48; mọi method `Async` + `CancellationToken ct = default`; timeout kết nối/ghi qua `CreateLinkedTokenSource` + `CancelAfter` (pattern Ch12/15); `IDisposable` (đóng socket + huỷ CTS) |
| Khung bản tin (framing) | ASCII từng dòng, kết thúc `\n` — ví dụ `TRIGGER`, `BUSY:1`, `DONE:OK:98.5:12.3:45.6:0.0`, `ACK`. Đơn giản, đọc được bằng mắt/telnet — đúng tinh thần code sách. KHÔNG nhị phân, KHÔNG length-prefix ở V4 |
| Chính sách lỗi | Mất kết nối giữa chu trình → ném `VisionEngineException` lớp con phù hợp (hoặc lớp mới `PlcCommunicationException` kế thừa từ hierarchy hiện có — xem Phương án dưới); KHÔNG tự reconnect ngầm ở V4 (reconnect có chính sách → V7) |
| `MeoVision.PlcTestReceiver` (project console mới, net48) | `TcpListener` lắng nghe cổng cấu hình được (mặc định ví dụ `50505`); in mọi bản tin nhận kèm timestamp `HH:mm:ss.fff`; gõ phím gửi ngược `TRIGGER`/`ACK` để chạy tay từng bước. KHÔNG reference project nào ngoài `MeoVision.Core` (đọc chung hằng tên tín hiệu) |
| Cấu hình | Host/port/timeout đọc từ JSON config hiện có của Application (không hardcode — magic number vào config/recipe) |

**Phương án đã cân nhắc — kiểu exception khi lỗi truyền thông:**
- **(A) Dùng lại `VisionEngineException` lớp con** — ít kiểu mới, nhưng sai ngữ nghĩa
  (lỗi PLC không phải lỗi vision engine).
- **(B — khuyến nghị) Thêm `PlcCommunicationException` riêng trong Core** — đúng ngữ nghĩa,
  cycle service phân nhánh retry rõ ràng; thêm 1 file nhỏ. → Chọn B trừ khi tác giả muốn
  tối giản số kiểu (ghi quyết định cuối cùng vào DESIGN_LOG khi triển khai).

**Kiểm thử:** unit test framing (parse/format bản tin — hàm THUẦN tách riêng để test tất
định, giống pattern `SimulatedEvaluator`); integration test in-process `TcpListener` ↔
`TcpPlcCommunication` (không cần app ngoài); test timeout/cancel.

**DoD:**
- [ ] Chạy `MeoVision.WorkStation.Demo` với `TcpPlcCommunication` ↔ `PlcTestReceiver`
      thật qua `127.0.0.1`: ≥ 8 chu trình, tín hiệu 4 pha in đúng thứ tự ở receiver.
- [ ] Rút receiver giữa chừng → demo báo lỗi rõ ràng, không treo, không crash.
- [ ] Unit + integration test pass qua `vstest.console.exe`; build 0 warning.

**Phụ thuộc:** không — chạy hoàn toàn trên loopback, không cần phần cứng.

---

### V5 — Recipe đa bước + versioning/backup vpp & config  *(checklist #4 — mở rộng theo DESIGN_LOG [7])*

**Mục tiêu:** một bản MeoVision chạy được nhiều máy/sản phẩm khác nhau CHỈ bằng recipe
(thư mục vpp + config) — sửa tại hiện trường không cần build lại, không cần Visual Studio;
mọi thay đổi recipe/vpp có vết dù nhà máy không có git; hỏng file/nhầm lẫn khôi phục được.

**Nguyên tắc ranh giới (chốt ở DESIGN_LOG [7], nhắc lại vì V5 là nơi hiện thực):**
`.vpp` = công cụ vision (tìm/đo/đọc mã); **exe = quyết định** (OK/NG, retry, ghép kết quả
xuyên bước) — nhưng tham số quyết định (ngưỡng, retry, hành vi fail) nằm trong recipe để
sửa được tại hiện trường. Script nhúng trong `.vpp` được PHÉP như van xả: chỉ việc thuần
vision, kết quả chỉ ra ngoài qua terminal, app coi `.vpp` là hộp đen.

**Phạm vi mã:**

| Hạng mục | Yêu cầu chi tiết |
|---|---|
| Schema recipe v2 — đa bước | `VisionRecipe` thêm `SchemaVersion` (int) + `Steps` (danh sách): mỗi bước gồm `Name`, `VppPath`, map input terminal (tên → nguồn: ảnh camera hoặc output bước trước), danh sách output terminal, ngưỡng pass/fail riêng, `FailurePolicy` (Stop/Retry/Continue). JSON cũ thiếu `SchemaVersion` → coi là v1, migrate = 1 bước duy nhất với terminal mặc định (`InputImage`, `Score`, `PartX`...); mỗi bước migrate = hàm thuần `MigrateV1ToV2(JObject)` test được |
| `TerminalNames` hết cứng | Từ hằng số → giá trị mặc định khi recipe không khai báo; Engine đọc tên terminal theo từng bước từ recipe |
| Engine chạy chuỗi bước generic | `CognexVisionEngine` load N ToolBlock theo `Steps`, chạy tuần tự, chuyển output→input giữa các bước theo map; **validate tên terminal NGAY LÚC LOAD recipe** (đối chiếu ToolBlock thật) — gõ sai tên phải fail sớm kèm tên bước + tên terminal, KHÔNG fail giữa ca. `SimulatedVisionEngine` parity: chạy cùng schema `Steps` không cần Cognex |
| Quyết định xuyên bước ở app | `InspectionCycleService` tổng hợp verdict từ kết quả các bước theo `FailurePolicy` — logic này KHÔNG nằm trong script vpp; tách class thuần (kiểu `SimulatedEvaluator`) test tất định |
| Backup vpp + config không cần git | Theo dõi checksum (SHA-256) từng `.vpp` + file recipe lúc load; phát hiện đổi so với lần trước → tự copy bản cũ sang `backup/{tên}.{yyyyMMdd_HHmmss}.*` TRƯỚC khi dùng bản mới, giữ tối đa N bản (config, mặc định 20), log rõ file nào đổi lúc nào. `IRecipeRepository.SaveAsync` cũng backup trước khi ghi đè |
| Restore | `RestoreAsync(string recipeName, string backupFileName, CancellationToken ct = default)`; UI tab Cấu hình: danh sách backup (cả recipe lẫn vpp) + nút khôi phục |
| Đổi model an toàn | `InspectionCycleService` chỉ cho đổi recipe khi KHÔNG giữa chu trình (cờ trạng thái + lock); yêu cầu giữa chừng → từ chối kèm lý do, KHÔNG xếp hàng ngầm |
| Audit | Log mỗi lần save/restore/phát-hiện-vpp-đổi: tên, version, checksum, timestamp — chưa cần user management (ngoài phạm vi code sách) |

**Phương án — nơi đặt logic versioning:**
- **(A — khuyến nghị) Trong `JsonRecipeRepository` hiện có** — đơn giản, ít file, đủ cho 1
  định dạng lưu trữ.
- **(B) Decorator `VersionedRecipeRepository` bọc ngoài** — sạch về SRP nhưng thêm tầng
  gián tiếp; chỉ đáng khi có ≥ 2 backend lưu trữ. → Chọn A (nguyên tắc "Simple" — chưa cần
  abstraction).

**Kiểm thử:** migrate v1→v2 (recipe cũ thành 1 bước, demo V0–V3 không vỡ); validate terminal
sai tên → lỗi lúc load nêu đúng bước/terminal; chuỗi 3 bước trên simulated engine với đủ 3
loại `FailurePolicy`; checksum đổi → backup đúng file + cắt bớt quá N bản; restore trả đúng
nội dung; đổi recipe giữa chu trình bị từ chối.

**DoD:**
- [ ] Recipe 3 bước chạy end-to-end trên `SimulatedVisionEngine`; thêm/bớt/đổi thứ tự bước
      CHỈ bằng sửa JSON — không build lại.
- [ ] Recipe v1 cũ load được (migrate tự động), toàn bộ demo V0–V3 chạy như trước.
- [ ] Sửa tay 1 file vpp (checksum đổi) → app tự backup bản cũ + log; restore từ UI xong
      chạy lại bình thường.
- [ ] Gõ sai tên terminal trong recipe → lỗi ngay lúc load, message nêu bước + terminal.
- [ ] Test pass, build 0 warning; chạm nội dung sách Ch13/14 (recipe/ToolBlock) → chạy
      `meovision-book-sync-check`.

**Phụ thuộc:** không — simulated đủ cho toàn bộ logic; chạy chuỗi ToolBlock Cognex thật cần
nhiều `.vpp` → gộp vào đợt phần cứng V8.

---

### V6 — Lưu trữ bền vững & truy vết & SPC cơ bản  *(checklist #8 + #5)*

**Mục tiêu:** kết quả sống sót qua restart; truy vết được "sản phẩm lúc 14:32 hôm qua vì
sao NG"; có số liệu chất lượng cơ bản (yield, Cpk).

**Phạm vi mã:**

| Hạng mục | Yêu cầu chi tiết |
|---|---|
| `SqliteInspectionResultStore : IInspectionResultStore` | `System.Data.SQLite` (net48). KHÔNG đổi interface (thiết kế V0 đã hứa "đổi sang SQLite không vỡ interface" — đây là lúc chứng minh). Schema: `Results(Id, Timestamp, RecipeName, Verdict, Score, PartX, PartY, PartAngle, Code, FailureReason, ImagePath NULL)` |
| Lưu ảnh OK theo tỷ lệ mẫu | Policy Ở CYCLE SERVICE (nguyên tắc V0: archiver không biết policy): `OkImageSampleRate` vào config (0 = tắt, 0.05 = 5%); random lấy mẫu phải tách hàm thuần + seed inject được để test tất định |
| Retention | Dọn định kỳ (khi khởi động + mỗi 24h): xoá bản ghi + ảnh quá `RetentionDays` (config, mặc định 30). Xoá là hành vi phá huỷ → log rõ số bản ghi/ảnh đã xoá |
| SPC cơ bản | Service thuần tính từ store: đếm OK/NG theo khoảng thời gian, yield %, mean/σ/Cpk của `Score` (công thức Cpk chuẩn `min(USL-μ, μ-LSL)/3σ` — USL/LSL từ recipe). Class thuần `SpcCalculator` KHÔNG phụ thuộc store — nhận `IReadOnlyList<double>`, test tất định |
| UI tab Lịch sử | Đọc từ SQLite thay in-memory; thêm ô thống kê yield/Cpk của khoảng đang lọc; click bản ghi NG có ảnh → hiện ảnh |

**Phương án — trình bày control chart:**
- **(A — khuyến nghị cho V6) Chỉ tính số liệu + xuất CSV** — vẽ chart bằng Excel/tool ngoài;
  ít code UI, đúng trọng tâm sách (kiến trúc, không phải charting).
- **(B) Vẽ control chart trong WinForms** — trực quan nhưng tốn nhiều code UI không dạy
  được gì về vision. → Chọn A; B để mở nếu sách cần hình minh hoạ.

**Kiểm thử:** store roundtrip (ghi → đọc → khớp); retention xoá đúng ngưỡng ngày; sampling
với seed cố định cho kết quả tất định; `SpcCalculator` đối chiếu giá trị Cpk tính tay.

**DoD:**
- [ ] Chạy demo N chu trình → tắt app → mở lại → tab Lịch sử còn nguyên dữ liệu.
- [ ] Bản ghi NG có ảnh mở được từ UI; bản ghi OK xuất hiện theo đúng tỷ lệ mẫu.
- [ ] Xuất CSV mở được bằng Excel; Cpk khớp tính tay trên cùng dữ liệu.

**Phụ thuộc:** package `System.Data.SQLite` (NuGet, net48 OK — kiểm chứng compile trước
khi viết nhiều).

---

### V7 — Chẩn đoán, watchdog 24/7, trạng thái kết nối  *(checklist #10 + #1/#7 phần giám sát)*

**Mục tiêu:** phần mềm biết và báo khi camera/PLC rơi kết nối; chính sách phục hồi rõ ràng;
chứng minh chạy dài không rò tài nguyên.

**Phạm vi mã:**

| Hạng mục | Yêu cầu chi tiết |
|---|---|
| Giám sát camera | Thêm vào `ICameraSource`: property `IsConnected` + event `ConnectionChanged` (EventArgs riêng). `CognexCameraSource`: kiểm chứng bằng reflection xem SDK có event mất kết nối không; nếu không → poll chu kỳ (config, mặc định 5s). `SimulatedCameraSource`: thêm hàm test-only ép rơi kết nối |
| Giám sát PLC | `TcpPlcCommunication` (V4): phát hiện socket đứt → `ConnectionChanged`; chính sách reconnect: thử lại theo backoff (1s→2s→5s, tối đa vào config), mỗi lần thử log rõ — KHÔNG im lặng |
| `IAlarmService` mở rộng | Bảng mã lỗi có cấu trúc (hằng số, không magic number): dải 1xx camera, 2xx PLC, 3xx engine, 4xx app — kèm message tiếng Việt + hướng xử lý ngắn |
| UI chip trạng thái | Tab Vận hành thêm 2 chip Camera/PLC (Xám=chưa kết nối, Xanh=OK, Đỏ=mất — cập nhật từ `ConnectionChanged`, nhớ marshal về UI thread WinForms qua `Invoke`) |
| Hành vi khi mất kết nối giữa chu trình | Chu trình đang chạy → fail có kiểm soát (alarm + NG kỹ thuật, KHÔNG treo watchdog); đang idle → chỉ chip đổi màu + alarm, không chặn UI |
| Soak test | Script/chế độ chạy demo liên tục ≥ 2 giờ simulated; theo dõi working set + handle count đầu/cuối (chênh lệch phải giải thích được) |

**Kiểm thử:** simulated camera rơi kết nối → alarm đúng mã + event bắn 1 lần (không bão
event); reconnect backoff đúng nhịp (inject clock/delay để test nhanh); chu trình đang chạy
gặp mất kết nối → kết thúc có kiểm soát.

**DoD:**
- [ ] Tắt `PlcTestReceiver` khi demo đang chạy → chip PLC đỏ + alarm 2xx + tự nối lại
      khi receiver bật lại (theo backoff, log đủ).
- [ ] Soak 2h: không crash, memory ổn định, số handle không tăng tuyến tính.
- [ ] Test pass, build 0 warning.

**Phụ thuộc:** V4 xong (giám sát PLC dựa trên `TcpPlcCommunication`). Camera vật lý giúp
kiểm chứng thật nhưng KHÔNG chặn (simulated đủ cho logic).

---

### V8 — Hiệu chỉnh pixel→mm, Teach UI, đóng gói Runtime  *(checklist #2 + #3 + #9)*

**Mục tiêu:** khép các mảnh cuối cùng cần phần cứng/license: kết quả ra mm thay vì pixel,
teach pattern từ UI, và bản deploy chạy trên máy chỉ có VisionPro Runtime.

**Phạm vi mã:**

| Hạng mục | Yêu cầu chi tiết |
|---|---|
| Calibration pixel→mm | Kiểm chứng reflection `CogCalibCheckerboardTool` (hoặc tool tương đương có thật trong SDK 9.0 — KHÔNG viết theo trí nhớ). Kết quả calibration lưu cùng recipe (đường dẫn file calib hoặc ma trận); `VisionResult` thêm `PartXMm/PartYMm` *nullable* — pixel giữ nguyên, không phá client cũ. Simulated: hệ số scale cố định cấu hình được |
| Hand-eye | NGOÀI PHẠM VI (không robot) — ghi rõ trong doc để không ai chờ |
| Teach UI | Hiện thực `CognexVisionEngine.TeachPatternAsync` (bỏ `NotSupportedException`): nhận `VisionTrainRegion` từ UI (kéo chọn vùng trên PictureBox), train pattern, lưu vào `.vpp`/recipe. Tuỳ chọn minh hoạ sâu: nhúng `CogRecordDisplay` ở UI (được phép theo DESIGN_LOG [4]) |
| Deployment | Thư mục `deploy/` + script gom: exe + DLL app (KHÔNG copy DLL Cognex — GAC của máy đích lo); `appsettings` mẫu theo máy; `DEPLOY_CHECKLIST.md` (cài VisionPro Runtime, license dongle, cấu hình GigE NIC jumbo frame, firewall) |
| Chạy thật end-to-end | `.vpp` tác giả dựng (QuickBuild; tên terminal khai báo trong recipe từ V5 — mặc định `InputImage`, `Score`, `PartX`, `PartY`, `PartAngle`, `Code`) + camera GigE vật lý → chạy trọn: trigger → grab → chuỗi bước theo recipe → kết quả mm → lưu vết. Đây cũng là lúc kiểm chứng chuỗi ĐA BƯỚC V5 trên Cognex thật (simulated đã chứng minh trước) |

**Kiểm thử:** simulated calibration scale đúng hệ số; teach trên simulated engine roundtrip
(teach xong → inspect ảnh synthetic chứa pattern → tìm thấy); deploy checklist đi thử trên
máy sạch (hoặc VM) — từng bước tick được.

**DoD:**
- [ ] Kết quả hiển thị mm khi recipe có calibration; vẫn pixel khi không có (không lỗi).
- [ ] Teach từ UI → inspect ngay sau đó dùng pattern vừa teach (simulated bắt buộc,
      Cognex thật khi có `.vpp` + camera).
- [ ] Bản deploy chạy trên máy không cài Visual Studio.

**Phụ thuộc (chặn một phần):** `.vpp` thật (việc tác giả — thao tác GUI QuickBuild, không
tự động hoá được); camera GigE vật lý; máy/VM có VisionPro Runtime để thử deploy.

---

## §4. Yêu cầu chi tiết của mã — áp dụng cho MỌI giai đoạn

Đây là "định nghĩa chất lượng" của từng dòng code viết mới (gộp từ `CLAUDE.md` MeoVision +
Style Guide sách — vi phạm là chưa xong việc, dù tính năng chạy):

| # | Yêu cầu | Ghi chú |
|---|---|---|
| 1 | Target **net48**, SDK-style csproj, `LangVersion 11`, `Nullable enable` | `Directory.Build.props` chung đã có |
| 2 | `Guard.ThrowIfNull(x, nameof(x))` trong mọi constructor | KHÔNG `ArgumentNullException.ThrowIfNull` — không tồn tại trên net48 (đã xác nhận CS0117) |
| 3 | Mọi method bất đồng bộ: hậu tố `Async` + `CancellationToken ct = default` | Cấm `.Result`/`.Wait()`/`Thread.Sleep` |
| 4 | Mọi lệnh gọi SDK/IO có timeout: `using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct); cts.CancelAfter(...)` | Pattern Ch12/15 của sách |
| 5 | Class giữ socket/CTS/SemaphoreSlim/event subscription → `IDisposable` | |
| 6 | Logic quyết định tách thành hàm/class **thuần** test được tất định | Noi gương `SimulatedEvaluator`; random/clock phải inject được |
| 7 | Không magic number — vào recipe hoặc JSON config | |
| 8 | Dòng ~90 ký tự; không `var` khi kiểu không rõ từ vế phải | Code sẽ trích vào sách |
| 9 | Project mới dùng `record`/`init` → copy `Properties/IsExternalInit.cs` | net48 thiếu type này |
| 10 | Build **0 warning** mức mặc định (không cần `TreatWarningsAsErrors` kiểu AM.AutoFrame) | |
| 11 | Test mới: xUnit + Moq + FluentAssertions, xác nhận pass THẬT qua `vstest.console.exe` | Không báo "pass" khi chưa chạy |
| 12 | API `Cog*` mới: kiểm chứng reflection/XML IntelliSense TRƯỚC khi viết | Không thấy = API bịa |
| 13 | File header 2 dòng (`// File: ... — Project: ...` + chương/mục sách) cho file sẽ trích vào sách | Khuyến khích, không bắt buộc |
| 14 | Code chạm nội dung đã in trong sách → chạy `meovision-book-sync-check`, báo tác giả | KHÔNG tự sửa sách |
| 15 | Quyết định kiến trúc mới trong lúc làm → thêm mục `[N]` cuối `DESIGN_LOG.md` | Một file duy nhất, không tách file mới |

---

## §5. Ma trận phụ thuộc phần cứng / license / việc-của-tác-giả

Những mục KHÔNG thể tự hoàn thành bằng code — cần theo dõi riêng để không "chờ ngầm":

| Phụ thuộc | Chặn gì | Ai / khi nào |
|---|---|---|
| File `.vpp` thật khớp `TerminalNames` | V2 chạy end-to-end thật; V8 teach/calib thật | Tác giả dựng bằng QuickBuild (GUI, không tự động hoá) |
| Camera GigE vật lý | `CognexCameraSource.GrabAsync()` thật; kiểm chứng giám sát camera V7 bằng thiết bị thật | Tác giả gắn camera |
| License VisionPro Runtime + máy/VM sạch | Thử deploy V8 | Tác giả chuẩn bị máy đích |
| Xác nhận vị trí thư mục project | Không chặn (đã ngầm định qua nhiều session) | Tác giả xác nhận 1 lần cho dứt điểm |

Mọi thứ khác (V4, V5, V6, phần lớn V7) chạy được **100% bằng simulation trên loopback** —
không có lý do chờ phần cứng để dừng tiến độ.

---

## §6. Thứ tự thực hiện & tiêu chí "hoàn thiện" tổng

**Thứ tự khuyến nghị: V4 (chốt nốt test) → V5 → V6 → V7 → V8.** Lý do: V4 đã triển khai +
kiểm chứng thật (DESIGN_LOG [6]), chỉ còn kiểm thử tự động; V5 (recipe đa bước — DESIGN_LOG
[7]) là nền cho "1 phần mềm nhiều máy" nên đi trước; V5/V6 độc lập phần cứng và cho giá trị
truy vết ngay; V7 cần V4; V8 gom mọi mục chờ phần cứng vào một giai đoạn cuối thay vì rải
rác chờ đợi.

Hoán vị chấp nhận được: V5 ↔ V6 (độc lập nhau); phần simulated của V8 (teach trên engine
giả lập) có thể kéo lên sớm nếu sách cần minh hoạ chương Teach trước.

**MeoVision được coi là "hoàn thiện" khi:**

1. Cả 10 hàng ở bảng §1 đạt ✅ hoặc có ghi chú "ngoài phạm vi" rõ ràng (hand-eye,
   phân quyền operator/engineer);
2. Toàn bộ demo chạy được ở 2 chế độ: **simulation thuần** (máy không license, cho độc
   giả sách) và **thật** (Cognex + camera + `.vpp`, khi phần cứng sẵn sàng);
3. Test suite pass qua `vstest.console.exe`, build 0 warning;
4. Soak test 2h (V7) đạt; deploy checklist (V8) đi được trên máy sạch;
5. Sách và code không còn điểm lệch chưa-báo-tác-giả (`meovision-book-sync-check` sạch).
