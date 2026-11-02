# PROJECT_STATUS — MeoVision

> Đọc file này TRƯỚC NHẤT mỗi session.

**Cập nhật lần cuối:** 2026-07-25 (Session 6 — tác giả gửi checklist 10 thành phần chuẩn phần mềm
vision công nghiệp, yêu cầu tiếp tục xây theo đó; + 3 việc khác: (1) gộp `docs/design-notes/` thành
1 file `DESIGN_LOG.md` — XONG, xem CHANGELOG Session 6; (2) chọn TCP/`127.0.0.1` làm phương tiện
truyền PLC thật + xây phần mềm nhận/test riêng — XONG, kiểm chứng thật 2 process (DESIGN_LOG [6]);
(3) xem lại RefVis-A lần nữa qua lăng kính 10 điểm checklist — XONG → quyết định recipe đa bước
(DESIGN_LOG [7]) + roadmap V4–V8 tại `docs/ROADMAP_HOAN_THIEN.md`. Sau đó tác giả yêu cầu thêm:
xem phần mềm tham khảo cần bổ sung form/giao diện nào — đã kiểm kê 21 form RefVis-A (Review mục
14), chọn xây 4 màn ưu tiên cao (Teach, xem ảnh NG, tab xu hướng, cấu hình lưu ảnh) + `UserLevel`
tối giản gác Teach — XONG, build+test+smoke-test thật (DESIGN_LOG [8]). Toàn bộ quyết định kiến
trúc nay nằm trong MỘT file `docs/design-notes/DESIGN_LOG.md`, đọc theo mục `[1]`-`[8]`, không
còn nhiều file `000N-*.md`.)

## Trạng thái tổng

| Hạng mục | Trạng thái |
|---|---|
| Vị trí project | 🟡 Tự chọn `C:\Users\dinhn\OneDrive\C\VS\MeoVision` (sibling AM.AutoFrame) — **chưa xác nhận với tác giả** (không chặn tiến độ — tác giả đã yêu cầu viết code tại đây nhiều lần) |
| Kiến trúc | ✅ net48, 1 process, không IPC (`DESIGN_LOG.md` mục [2]) — kiểm chứng bằng GitHub thật (mục [3]) + RUNTIME thật (mục [5], không chỉ compile) — WinForms mặc định không ép buộc |
| Sách ↔ code | ✅ Sách cập nhật theo code (mục [4]) — 5 điểm lệch đã biết, ghi trong mục [4], **đã áp dụng vào Chương 14/15 ở Session 18 của dự án sách** |
| `.sln` + `.csproj` thật | ✅ 9 project (7 src + 2 test — thêm `MeoVision.PlcTestReceiver` Session 6), build 0 lỗi 0 warning |
| **V0** — Core+Engine.Simulated+Application+WorkStation.Demo skeleton | ✅ Build sạch; 15/15 unit test pass (`vstest.console.exe`); chạy `.exe` thật → 1 chu trình, ảnh NG thật vẽ+lưu đĩa |
| **V1** — MeoVision.UI (WinForms) | ✅ `FormMain` (4 tab: Vận hành/Lịch sử/Xu hướng/Cấu hình) + `FormLogin`/`FormTeach`/`FormNgImages`/`FormImageRetentionSettings` (Session 6). Build sạch; smoke-test thật dựng cả 5 form qua DI + `CreateControl()` — cả 5 `OK`. *Chưa xác minh trực quan bằng mắt* (computer-use không resolve được exe tự build, chưa đăng ký Start Menu) |
| **V2** — MeoVision.Engine (Cognex thật) | ✅ Compile-verify: build thành công chống lại DLL Cognex VisionPro 9.0 CR2 THẬT cài trên máy (GAC). Nay thêm `CognexCameraSource` (GigE thật qua `CogFrameGrabberGigEs`/`ICogAcqFifo`, Ch6) — cũng compile-verify thành công, chưa có camera GigE vật lý để chạy hết `GrabAsync()` thật. `CognexVisionEngine` nhận `ICameraSource?` tuỳ chọn, tự thu ảnh khi `VisionJobRequest.SourceImage` rỗng |
| **V3** — WorkStation.Demo mô phỏng PLC | ✅ Đã refactor dùng `IPlcCommunication` hình thức hoá (không còn hardcode `Console.WriteLine` trực tiếp) — chạy lại `.exe` thật xác nhận hành vi không đổi: 8 chu trình, 5 OK/3 NG, ảnh NG thật lưu đĩa |
| Camera/Đèn/Truyền thông (Session 5) | ✅ `ICameraSource`+`CognexCameraSource`(thật, compile-verify)+`SimulatedCameraSource`(đơn giản có chủ đích); `IPlcCommunication`+`SimulatedPlcCommunication`(dùng cho demo)+`NotImplementedPlcCommunication`(stub cũ, sẽ thay bằng `TcpPlcCommunication` ở Session 6) |
| Truyền thông PLC thật + test receiver (Session 6) | ✅ `TcpPlcCommunication` (WorkStation.Demo) + `MeoVision.PlcTestReceiver` (project độc lập hoàn toàn) — kiểm chứng thật 2 process qua `127.0.0.1:5050`: 8/8 chu trình, đúng thứ tự TRIGGER/BUSY/DONE/ACK, OK=5/NG=3 khớp bản Simulated cũ (DESIGN_LOG [6], kèm bài học BOM PowerShell). Còn nợ: unit test tự động cho framing (xUnit), xem roadmap V4 |
| Recipe đa bước (quyết định Session 6) | 🟡 ĐÃ QUYẾT (DESIGN_LOG [7] — phương án B: chuỗi ToolBlock theo recipe, `.vpp`=vision/exe=quyết định, backup checksum không cần git) — triển khai ở V5 theo `docs/ROADMAP_HOAN_THIEN.md` |
| 4 màn hình UI mới + UserLevel (Session 6) | ✅ `FormTeach` (Ch14 §14.4.2), `FormNgImages` (Ch14 §14.5), tab Xu hướng (Ch16 §16.4), `FormImageRetentionSettings` (Ch14 §14.5.2) + `UserLevel`/`ICurrentUserService` tối giản gác Teach (DESIGN_LOG [8]). Build 0 lỗi 0 warning, 21/21 test pass, smoke-test 5 form OK |
| Skill/agent | ✅ 4 skill + 1 agent |
| Review RefVis-A | ✅ Lần 1: `Review_RefVis-A.md` (12 mục). Lần 2 (Session 6, 10 điểm checklist): mục 13 (backup/SPC/watchdog/comm). Lần 3 (Session 6, kiểm kê UI): mục 14 (21 form → 18 nhóm chức năng, bảng ưu tiên) |

## Lộ trình

- [x] **V0** — MeoVision.Core + Engine.Simulated + Application + WorkStation.Demo skeleton (console, chạy được không cần Cognex/license)
- [x] **V1** — MeoVision.UI (WinForms live view + Result)
- [x] **V2** — Engine thật gọi Cognex VisionPro SDK (compile-verify; chạy thật chờ `.vpp` do tác giả dựng + camera GigE vật lý)
- [x] **V3** — WorkStation.Demo mô phỏng chu trình PLC↔Vision (nay qua `IPlcCommunication` hình thức hoá)

## Việc còn lại (không chặn — làm khi tác giả sẵn sàng)

1. **Tác giả xác nhận vị trí thư mục** — vẫn chưa hỏi/trả lời trực tiếp, chỉ ngầm định qua việc tiếp tục viết code tại đây.
2. **Dựng 1 file `.vpp` thật bằng QuickBuild** khớp `MeoVision.Engine.TerminalNames` (`InputImage`,
   `Score`, `PartX`, `PartY`, `PartAngle`, `Code`) để chạy thử V2 end-to-end (không tự động hoá
   được — thao tác GUI QuickBuild).
3. **Gắn camera GigE vật lý** để chạy hết `CognexCameraSource.ConnectAsync()`→`GrabAsync()` thật
   (hiện chỉ compile-verify — đúng như V2's TerminalNames, chưa có phần cứng để chạy hết).
4. ~~Chọn phương tiện truyền PLC thật~~ — **ĐÃ CHỌN + ĐÃ TRIỂN KHAI: TCP qua `127.0.0.1`**
   (Session 6, `DESIGN_LOG.md` mục [6]) — `TcpPlcCommunication` + `MeoVision.PlcTestReceiver`
   chạy thật, kiểm chứng 2 process. Còn nợ V4: kiểm thử tự động (roadmap V4 phần "Còn lại").
5. Cập nhật `output/Chapter_*.md` của sách theo 5 điểm lệch đã ghi trong `DESIGN_LOG.md` mục [4] —
   **ĐÃ ÁP DỤNG** vào Chương 14/15 ở Session 18 của dự án sách (chạy skill
   `meovision-book-sync-check` cho các lệch MỚI phát sinh sau đó, nếu có).
6. ~~Màn hình Teach~~ — **ĐÃ CÓ `FormTeach`** (Session 6, DESIGN_LOG [8]), nhưng chỉ chạy hết với
   `SimulatedVisionEngine` (log, không ghi file). `CognexVisionEngine.TeachPatternAsync` vẫn throw
   `NotSupportedException` có chủ đích — cần `.vpp` thật + camera thật để làm tiếp (V4+, mục #2/#3
   dưới đây vẫn là điều kiện chặn).
7. (Tuỳ chọn) Xác minh trực quan MeoVision.UI (nay đã 5 form: FormMain 4 tab + 4 dialog mới) bằng
   cách tự mở app — computer-use không resolve được exe chưa đăng ký Start Menu trong session này;
   đã bù bằng smoke-test `CreateControl()` qua DI thật (DESIGN_LOG [8]), nhưng chưa ai THẤY/CLICK
   qua UI thật.
8. (Tuỳ chọn, đợt sau) 3 màn UI mức Trung bình chưa làm: Nhóm E (CRUD recipe đầy đủ), F
   (calibration UI), H (form IP/port PLC/MES) — xem Review RefVis-A mục 14, DESIGN_LOG [8] phần
   "Không làm trong phiên này".
9. (Session 6) Rà theo 10 điểm checklist tác giả gửi — **lộ trình chi tiết đã chốt khung tại
   `docs/ROADMAP_HOAN_THIEN.md`** (bảng đối chiếu 10 thành phần ↔ hiện trạng + giai đoạn
   V4–V8 kèm yêu cầu mã chi tiết/DoD/test từng giai đoạn). Xem lại RefVis-A lần 2 CŨNG XONG →
   ra quyết định recipe đa bước (`DESIGN_LOG.md` mục [7], V5 roadmap viết lại theo đó).
   Việc còn lại của mục này = thực thi roadmap: V4 (chốt nốt kiểm thử) → V5 (recipe đa bước
   + backup vpp/config) → V6 (SQLite + SPC/Cpk) → V7 (watchdog camera/comm 24/7) → V8
   (calib pixel→mm + teach + deploy).

## Quyết định đã chốt (xem lý do đầy đủ trong `docs/design-notes/DESIGN_LOG.md`)

- **net48, 1 process** — không tách Engine/Application qua IPC. Kiểm chứng bằng runtime thật
  (mục [5]), không chỉ lý luận backward-compat.
- **WinForms mặc định cho UI** — không phải ép buộc, WPF khả thi nếu muốn đổi (xem mục [3]).
- **Sách cập nhật theo code** — không phải ngược lại (mục [4], đảo ngược nguyên tắc ban đầu).
- **"Đèn" = tham số strobe trên `VisionRecipe`** (Ch6 §6.2.5), KHÔNG phải đèn tháp báo trạng thái
  kiểu AM.AutoFrame `ILightController` — 2 khái niệm khác nhau (mục [5]).
- **Truyền thông PLC**: interface trung lập phương tiện (`IPlcCommunication`) theo đúng tinh thần
  sách Ch15; phương tiện thật = TCP/`127.0.0.1` (mục [6], Session 6).
- Không copy bất kỳ dòng code nào từ RefVis-A/AM.AutoFrame/repo GitHub tham khảo — chỉ tham khảo
  pattern/bài học, viết lại hoàn toàn mới.
- Không `TreatWarningsAsErrors`/`AnalysisMode=All` khắt khe như AM.AutoFrame, nhưng vẫn giữ 0
  warning thật (đã dọn sạch CS8618/CS8602/CA1031 có chủ đích trong quá trình viết V0-V3).
- **Design-notes: MỘT file duy nhất** (`docs/design-notes/DESIGN_LOG.md`), không tách file
  `NNNN-tieu-de.md` nữa (Session 6) — quyết định mới thêm mục vào cuối file này.
- **Recipe đa bước, `.vpp` = vision / exe = quyết định** (mục [7], Session 6 — bài học
  RefVis-A xem lần 2): chuỗi ToolBlock khai báo trong recipe (1 phần mềm nhiều máy, sửa hiện
  trường không build lại); tham số quyết định ra config; script trong `.vpp` chỉ là van xả
  thuần vision qua terminal; backup vpp/config bằng checksum thay git (nhà máy không mạng);
  `TerminalNames` thành mặc định, khai báo được theo bước trong recipe — triển khai V5.
- **4 màn UI ưu tiên cao + `UserLevel` tối giản** (mục [8], Session 6 — theo kiểm kê 21 form
  RefVis-A): chỉ xây màn nào vừa (a) đã có chương sách thiết kế/viết code sẵn nhưng thiếu UI, vừa
  (b) cần thật cho trạm 1 camera 1 sản phẩm — không xây theo kiểu "RefVis-A có gì thêm nấy". 2 cấp
  Operator/Engineer (không phải 5 cấp AM.AutoFrame), mật khẩu Engineer đọc config, phiên chỉ sống
  trong bộ nhớ.
