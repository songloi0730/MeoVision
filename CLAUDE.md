# ⚠ CẤU TRÚC MỚI TỪ 2026-10-14 (DESIGN_LOG [9], [10]) — ĐỌC TRƯỚC PHẦN DƯỚI

Mã V0–V3 đã chuyển vào `legacy/` (không thuộc solution); phần mô tả cấu trúc/bất biến cũ bên dưới
CHỈ còn giá trị tham khảo. Hiện hành: net48 x64, **VisionPro bắt buộc, không còn engine giả lập**,
`TreatWarningsAsErrors=true`. Solution `MeoVision.sln`:
`src/MeoVision.Core` (không Cognex) · `src/MeoVision.Vision` (DUY NHẤT chạm Cognex xử lý ảnh) ·
`src/MeoVision.App` (vòng đời, project, bộ máy lệnh, giao tiếp — không Cognex) ·
`src/MeoVision.UI` (WinForms, composition root, exe `MeoVision.exe`) ·
`tests/MeoVision.App.Tests` (unit) · `tests/MeoVision.Vision.Tests` (tích hợp, chạy THẬT VisionPro).
Kế hoạch: Phần VI sách (Ch17–24) xây lần lượt — Ch17 XONG code + XONG chương sách (2026-10-15).
Code trong sách tiêm NGUYÊN VĂN từ repo này: sửa file đã trích (danh sách trong PROJECT_STATUS)
→ chạy `tools/inject_code.py` của dự án sách; giữ dòng ≤ ~95 ký tự.
Build: MSBuild VS2022 `MeoVision.sln -restore`; test: vstest.console `/Platform:x64`.
**Ngôn ngữ (tác giả chốt 2026-10-19):** tên lớp/hàm/khoá và MỌI giá trị trong file cấu hình (machine.json,
product.json, appsettings.json, JSON trong test) viết tiếng Anh không dấu — tránh lỗi mã hoá/font/PLC/MES.
Tiếng Việt chỉ ở `languages/vi-VN.json` và comment/XML doc. Nạp `.vpp` LUÔN qua `ScriptDialogGuard`
(DESIGN_LOG [18]: script hỏng → hộp thoại modal treo luồng).

# CLAUDE.md — MeoVision

> Chương trình xử lý ảnh công nghiệp minh hoạ cho sách "Xử lý ảnh công nghiệp với Cognex
> VisionPro" (`C:\Users\dinhn\OneDrive\Study\Chung\VisionPro_Image_Processing_Book`) — nhưng từ
> mục [4] của `DESIGN_LOG.md`, MeoVision ưu tiên "giống phần mềm thật" hơn "khớp sách 100%"; sách
> cập nhật THEO code khi lệch, không phải ngược lại. Toàn bộ quyết định kiến trúc nằm trong MỘT
> file `docs/design-notes/DESIGN_LOG.md`, đọc theo mục: [2] (kiến trúc net48, 1 process — HIỆN
> HÀNH) → [3] (kiểm chứng net48 bằng 6 repo GitHub thật + WinForms mặc định/WPF khả thi) → [4]
> (sách theo code + ranh giới Cognex làm rõ lại + **V0-V3 đã có code thật, build/test/chạy thật**
> — không còn ở giai đoạn "chỉ có kế hoạch") → [5] (camera/đèn/truyền thông + runtime-proof net48)
> → [6] (mới nhất). [1] vẫn còn giá trị cho phần "vì sao ranh giới IVisionEngine tồn tại" nhưng
> phần "tách process/IPC" ĐÃ BỊ ĐẢO NGƯỢC ở [2] — đừng làm theo phần đó.

## ⚡ BẮT BUỘC đọc theo thứ tự

1. `PROJECT_STATUS.md` — trạng thái hiện tại, đang ở bước nào trong lộ trình
2. `docs/design-notes/DESIGN_LOG.md` — **MỘT file duy nhất**, mọi quyết định kiến trúc theo thứ tự
   thời gian (mục [1]-[6], mới nhất ở cuối). Không còn tách nhiều file `NNNN-tieu-de.md` — quyết
   định mới thêm vào CUỐI file này (mục `[N] YYYY-MM-DD — Tiêu đề`), không tạo file mới.
   - Mục [1]: bối cảnh nghiên cứu gốc — đọc để hiểu *vì sao* có ranh giới `IVisionEngine`, bỏ qua
     phần "tách process/IPC" đã lỗi thời (đảo ngược ở [2])
   - Mục [2]: kiến trúc HIỆN HÀNH — net48, 1 process, không IPC
   - Mục [3]: kiểm chứng net48 bằng 6 repo GitHub thật + WinForms mặc định/WPF khả thi
   - Mục [4]: sách cập nhật theo code (không ngược lại), ranh giới Cognex làm rõ lại (Engine=xử
     lý, UI=được phép nhúng hiển thị), bảng trạng thái thật V0-V3, 5 điểm code lệch sách cần biết
   - Mục [5]: kiểm chứng net48 bằng RUNTIME thật (dựng object Cognex thật, không chỉ compile) +
     `ICameraSource`/`IPlcCommunication` (camera/đèn strobe/truyền thông PLC — đèn = tham số
     strobe trên recipe, KHÔNG phải đèn tháp báo trạng thái kiểu AM.AutoFrame)
   - Mục [6]: mới nhất — xem `PROJECT_STATUS.md`/`CHANGELOG.md` để biết đang làm gì
3. `reference/Outline_To_Book_Mapping.md` — code này minh hoạ đúng chương/mục nào của sách
4. File này (CLAUDE.md) — quy tắc code cụ thể

## Dự án là gì?

**MeoVision** — phần mềm chạy thật minh hoạ cho sách VisionPro, khác "MeoFrame" ở sách trước (chỉ
là tên minh hoạ dùng lại qua nhiều chương, không phải code chạy được). Từ `DESIGN_LOG.md` mục [4]:
ưu tiên "giống phần mềm sản xuất thật" hơn "trích dẫn khớp sách" — sách cập nhật theo code khi hai bên lệch nhau.
V0 (Core+Engine.Simulated+Application+WorkStation.Demo), V1 (UI WinForms), V2 (Engine Cognex thật,
compile-verify) và V3 (mô phỏng bắt tay PLC) **đã viết xong, build/test/chạy thật** — xem
`PROJECT_STATUS.md` để biết chi tiết bằng chứng.

## ⚡ Nguyên tắc bất biến (không đổi khi chưa bàn lại)

| Nguyên tắc | Vì sao |
|---|---|
| **Toàn bộ solution target net48 (.NET Framework)** | VisionPro chỉ hỗ trợ chính thức .NET Framework; 1 process duy nhất — không IPC, không rủi ro cross-framework (đảo ngược mục [1], xem [2]). Kiểm chứng lại bằng 6 project Cognex thật trên GitHub (v4.6–v4.8.1, 0/6 dùng .NET hiện đại) — xem [3] |
| **UI: WinForms mặc định** (không còn là ép buộc) | Mục [3] tìm thấy bằng chứng thật (`RefVis-M`) rằng WPF + `WindowsFormsHost` cũng chạy được trên net48 — chọn WinForms vì đơn giản hơn cho ví dụ sách + khớp guide chính thức Cognex, không phải vì WPF không khả thi |
| Chỉ `MeoVision.Engine` được đụng kiểu **xử lý ảnh** Cognex (`CogToolBlock`, `ICogImage`, `CogSerializer`...) | Ranh giới TRONG PROCESS (assembly), không phải process OS; mọi project khác chỉ thấy `IVisionEngine`/`VisionResult`. `UI` được phép (tuỳ chọn, chưa dùng ở V1) nhúng thêm control **hiển thị** Cognex (`CogRecordDisplay`...) để minh hoạ sâu — làm rõ lại ở mục [4], tránh mâu thuẫn với ý ban đầu Ch5/Ch14 |
| Mọi tính năng mới trên `IVisionEngine` phải có cả 2 bản: `MeoVision.Engine` (thật) và `MeoVision.Engine.Simulated` (giả lập) | "Simulation parity" — máy đọc sách không có license VisionPro vẫn chạy demo được |
| **Sách cập nhật THEO code, không phải ngược lại** (đảo ngược tại mục [4]) | Ưu tiên "giống phần mềm thật" — code được phép khác sách (5 điểm đã biết, xem mục [4]); phát hiện lệch mới → chạy `meovision-book-sync-check`, báo tác giả quyết định sửa sách, KHÔNG tự sửa sách từ đây |
| Không thêm tính năng "cho đủ" ngoài lộ trình V0-V3 | Ưu tiên rõ ràng/dễ đọc + **chạy chắc chắn được** hơn kiến trúc "đẹp" — nhưng "giống phần mềm thật" (mục [4]) vẫn cho phép DI/logging/config JSON/exception hierarchy thật, không phải lý do để thêm tính năng ngoài lộ trình |

## Cấu trúc solution — ĐÃ CÓ CODE THẬT (không còn là kế hoạch)

`MeoVision.sln` (SDK-style `.csproj` mọi project, `Directory.Build.props` chung định nghĩa
`$(CognexVisionProDir)` + `LangVersion 11` + `Nullable enable`). Build bằng MSBuild thật
(`dotnet` CLI không build được net48 thuần) — xem `PROJECT_STATUS.md` mục lệnh build/test cụ thể.

```
MeoVision.Core                 (net48) — IVisionEngine, ICameraSource (thu ảnh, tách riêng khỏi
                                          IVisionEngine — Ch1 Hình 1.2 "chụp" khác "xử lý"),
                                          IPlcCommunication (4 tín hiệu Trigger/Busy/Done/Ack, trung
                                          lập phương tiện truyền — Ch15), CameraTriggerMode
                                          (FreeRun/Auto/Semi/Manual, khớp Bảng 6.2), VisionResult,
                                          VisionRecipe (+ TriggerMode/StrobeEnabled/StrobeDelayUs/
                                          StrobePulseDurationUs — tham số camera/đèn, chỉ Engine
                                          thật đọc), VisionCheckResult, VisionJobRequest,
                                          VisionTrainRegion, VisionEngineException (+3 lớp con),
                                          Guard.ThrowIfNull (net48 không có
                                          ArgumentNullException.ThrowIfNull — helper thay thế, dùng
                                          Guard.ThrowIfNull(x, nameof(x)) ở MỌI project khác thay vì
                                          gọi ArgumentNullException.ThrowIfNull trực tiếp). KHÔNG Cognex.
MeoVision.Engine                (net48, x64) — DUY NHẤT reference Cognex.VisionPro.* thật (GAC, xem
                                          csproj) — CognexVisionEngine implements IVisionEngine
                                          (nhận ICameraSource? tuỳ chọn qua constructor — dùng khi
                                          VisionJobRequest.SourceImage rỗng), CognexCameraSource
                                          implements ICameraSource (CogFrameGrabberGigEs/
                                          ICogAcqFifo/Semi trigger + strobe — Ch6, cần
                                          Cognex.VisionPro.FGGigE.dll thêm). Mọi API Cog* đã kiểm
                                          chứng bằng reflection trước khi viết (kể cả runtime thật —
                                          xem DESIGN_LOG.md mục [5] phần "kiểm chứng net48 chạy
                                          thật, không chỉ compile")
MeoVision.Engine.Simulated      (net48) — SimulatedVisionEngine + SimulatedEvaluator (đánh giá THUẦN,
                                          tách khỏi phần random để test được tất định) +
                                          SyntheticPartRenderer (vẽ ảnh thủ tục bằng System.Drawing);
                                          SimulatedCameraSource (khung xám trơn, CỐ TÌNH đơn giản —
                                          không lặp lại logic SyntheticPartRenderer, tác giả yêu cầu
                                          mô phỏng phần camera không cần cầu kỳ vì test bằng camera
                                          thật là chính)
MeoVision.Application           (net48) — InspectionCycleService (cycle+retry theo FailureReason),
                                          IRecipeRepository (JSON/Newtonsoft.Json),
                                          IInspectionResultStore (in-memory, đổi sang SQLite sau
                                          không vỡ interface), IAlarmService, INgImageArchiver
                                          (chỉ lưu Reject/Warning/Error — policy ở cycle service,
                                          KHÔNG ở archiver), Auth.UserLevel/ICurrentUserService
                                          (2 cấp Operator/Engineer tối giản, phiên trong bộ nhớ,
                                          gác FormTeach — DESIGN_LOG.md mục [8]),
                                          ServiceCollectionExtensions DI
MeoVision.UI                    (net48 WinForms) — FormMain 4 tab (Vận hành/Lịch sử/Xu hướng/Cấu
                                          hình), PictureBox+GDI+ overlay (không đụng Cognex ở V1 —
                                          xem bảng nguyên tắc phía trên), Timer-driven run loop, tab
                                          Xu hướng tự vẽ GDI+ (rolling window, không thư viện chart
                                          ngoài). + 4 dialog (mục [8]): FormLogin, FormTeach (chọn
                                          vùng train bằng kéo chuột, KHÔNG nhúng full ToolBlock
                                          editor), FormNgImages (lọc ảnh NG theo ngày),
                                          FormImageRetentionSettings (ghi thẳng appsettings.json)
MeoVision.WorkStation.Demo      (net48, console) — PlcHandshakeSimulator: orchestrator gọi
                                          IPlcCommunication cho 4 tín hiệu thô quanh
                                          InspectionCycleService thật, 2 watchdog độc lập (PLC rộng
                                          hơn CycleTimeoutMs nội bộ). Transport: SimulatedPlcCommunication
                                          (console, demo độc lập) hoặc TcpPlcCommunication (TCP qua
                                          127.0.0.1, mặc định — DESIGN_LOG.md mục [6]), chọn qua
                                          appsettings.json PlcCommunication:Transport
MeoVision.PlcTestReceiver       (net48, console) — công cụ thủ công đóng vai PLC/gateway để kiểm
                                          tra TcpPlcCommunication qua 127.0.0.1:5050; KHÔNG
                                          reference project MeoVision nào (kể cả Core) — cố tình độc
                                          lập để là công cụ kiểm tra khách quan (mục [6])
tests/MeoVision.Engine.Simulated.Tests, tests/MeoVision.Application.Tests — xUnit+Moq+FluentAssertions,
                                          21/21 pass (xác nhận thật qua vstest.console.exe)
```

Không còn `MeoVision.Ipc` — không còn ranh giới process nào cần vượt qua.

## Quy tắc code (áp dụng khi viết ví dụ sẽ trích vào sách)

- **PascalCase** cho class/method/property; **Async suffix** cho mọi method bất đồng bộ; **_camelCase**
  cho field riêng — đúng convention Style Guide của sách (đã dùng suốt 16 chương).
- **Không dùng `var`** khi kiểu không rõ ràng ngay từ vế phải — code trích vào sách cần đọc được
  không cần chạy IDE.
- **Giới hạn ~90 ký tự/dòng** — khớp khổ trang khi in Code X.Y trong sách, tránh code bị ngắt dòng
  xấu khi convert sang PDF/docx.
- **`Guard.ThrowIfNull(x, nameof(x))`** (từ `MeoVision.Core`) trong constructor — KHÔNG dùng
  `ArgumentNullException.ThrowIfNull` trực tiếp, API đó chỉ có từ .NET 6+, không tồn tại trên
  net48 (xác nhận thật bằng lỗi biên dịch CS0117 khi thử — đã sửa toàn bộ project sang `Guard`).
  `CancellationToken ct = default` cho mọi method bất đồng bộ,
  `using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct)` cho mọi lệnh gọi SDK có
  timeout — đúng pattern sách đã dạy (Chương 12/15), làm gương cho độc giả.
- **Record + net48**: mọi project khai báo `record`/tham số `init` cần polyfill riêng
  `System.Runtime.CompilerServices.IsExternalInit` (xem `Properties/IsExternalInit.cs` ở
  `Core`/`Application`/`Engine.Simulated`) — trình biên dịch cần type này tồn tại (bất kỳ ai định
  nghĩa), net48 không có sẵn. Copy y nguyên file này sang project mới nếu project đó cũng khai
  báo record.
- **Không cần `TreatWarningsAsErrors`/`AnalysisMode=All` khắt khe như AM.AutoFrame** — đây là code
  minh hoạ, không phải phần mềm điều khiển máy sản xuất thật. Vẫn giữ 0 warning ở mức mặc định.
- **File header** giống quy ước AM.AutoFrame (ngắn gọn hơn — không bắt buộc, chỉ khuyến khích cho
  file sẽ trích nguyên văn vào sách):
  ```csharp
  // File: {FileName}.cs — Project: {ProjectName}
  // Minh hoạ: sách "VisionPro", Chương {N}, mục {N.M}
  ```

## Khi đổi code ảnh hưởng nội dung đã in trong sách

Chạy skill `meovision-book-sync-check` — grep các chương `output/Chapter_*.md` liên quan, cảnh
báo nếu code sách và code MeoVision lệch nhau. KHÔNG tự ý sửa sách từ đây — báo lại tác giả, để
tác giả (hoặc phiên làm việc trên dự án sách) quyết định cập nhật chương nào.

## Anonymization — áp dụng đúng quy ước đã có của dự án sách

Nếu cần tham khảo thêm 1 dự án vision thật khác: dùng bí danh `RefVis-X` (tiếp theo RefVis-A đã
dùng), bảng đối chiếu CHỈ nằm trong `docs/private/RefVis_Alias_Map.md` của dự án SÁCH (không tạo
bảng riêng ở đây) — không bao giờ tên thật xuất hiện trong bất kỳ file nào commit ở MeoVision.

## Cuối mỗi session

1. Cập nhật `PROJECT_STATUS.md` — đang ở bước nào (V0-V4), gì xong/gì chưa
2. Thêm entry `CHANGELOG.md`
3. Commit (khi tác giả yêu cầu rõ ràng — không tự ý commit)
