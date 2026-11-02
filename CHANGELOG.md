# CHANGELOG — MeoVision

## Session 27 — 2026-11-01 — Sửa treo "Not Responding" khi để yên lâu (DESIGN_LOG [30])

- **Nguyên nhân (đọc chồng gọi native của tiến trình treo + spike):** COM tự tháo `cogxImagingDevice.dll` sau một thời gian
  không dùng; lúc tháo, DLL đó `SendMessage` tới luồng giao diện trong khi giữ loader lock → khoá chết với luồng giao diện.
- **Sửa:** `Vision/Runtime/NativeModulePin.cs` ghim mọi `cogx*.dll` (không bao giờ tháo); `Program.cs` ghim lúc khởi động +
  mỗi 30 s. Test 179 → 180 (`NativeModulePinTests`). Phát hành `2.0.261101`.
- **Repo GitHub** (2026-11-02): `git init` + `songloi0730/MeoVision` (private). `.gitignore` thêm `dist/`, `Data/`, `*.dmp`,
  `reference/`, `docs/backups/`. Tên repo tham khảo trong CHANGELOG, DESIGN_LOG, CLAUDE.md, UI_Spec_v1, mockup v1 và
  csproj legacy đổi thành bí danh RefVis-E/G/K/L/M/N/O/P (bảng đối chiếu chỉ ở `docs/private` của sách).

## Session 26 — 2026-10-31 — Không gửi lý do cho máy; lệnh kiểm tra / dẫn hướng (DESIGN_LOG [29])

- Bản tin trả máy giữ đủ số trường nhưng **trường lý do luôn trống** (TCP), word `Reason` của PLC luôn 0 — cả NG lẫn ERR.
  Lý do vào log, màn Giao tiếp (cột mới) và CSV (cột `error`: khoá lỗi, hoặc `LIMIT:<output>` / `REJECT` khi NG).
- `product.json`: thêm `kind` cho từng lệnh — `Inspect` (mặc định, máy test/AOI, được bỏ qua có kiểm soát) hoặc `Guide`
  (vision trả X, Y, góc cho chuyển động — **không bao giờ bỏ qua**, chặn ở bộ máy chạy lệnh và trên form).
- Form Sản phẩm thêm cột Loại. Test 177 → 179. Phát hành lại `2.0.261031`, bản cài ổ D cập nhật.

## Session 25 — 2026-10-31 — Phân tích khối chức năng (DESIGN_LOG [28]) + bỏ qua có kiểm soát, dải cảnh báo, Giao tiếp, Hiệu chuẩn

- **Phân tích:** `docs/PHAN_TICH_KHOI_CHUC_NANG.md` — 22 khối × 6 chương trình tham khảo + MeoVision; 12 cách làm phổ biến
  đánh giá theo góc máy công nghiệp (thăm dò PLC, nuốt lỗi, hộp thoại, bypass, 999, tắt timeout tool...); đề xuất D1–D10.
- **D1 Bỏ qua kiểm có kiểm soát** (BYP / word 4 / bit 7, Kỹ sư, lý do, hết hạn N giờ, mất khi khởi động lại, nhật ký).
  **Bảng bit PLC đổi:** thêm `Bypass` = bit 7 (đọc 8 bit thay vì 7).
- **D3 + D4 Dải cảnh báo** dưới thanh nút + gợi ý "cách xử lý" (`*.hint`) trong danh sách cảnh báo và tooltip Nhật ký.
- **D2 Màn Giao tiếp** (bản tin vào/ra, bit PLC trực tiếp, gửi thử khi OFFLINE). **D5 Màn Hiệu chuẩn** (RMS, sai số từng
  điểm, dùng lại bản cũ, bỏ phiên dở). Nút Hiệu chuẩn, Giao tiếp không còn báo "đang xây".
- Lịch sử: lọc + đếm `BYPASSED`. Thanh nút vừa màn 125% (tên sản phẩm không còn bị đẩy vào nút tràn).
- Phát hành `2.0.261031`; bản cài `D:\MeoVision\2.0.261031\` (nâng cấp bằng sao lưu/khôi phục, `settings.json` đi theo).
- Test 167 → 177. Chạy thật: xem DESIGN_LOG [28] "Kết quả".

## Session 24 — 2026-10-30 — Ảnh/log sang ổ D + màn Lưu trữ, hai lớp cấu hình, sửa Thiết lập lệnh không mở (DESIGN_LOG [27])

- **Lỗi tác giả gặp:** Thiết lập lệnh (khu vực chỉnh tool) không mở — `Access denied` khi xoá bản nháp cũ vì thư mục Data
  nằm trong OneDrive. Sửa `ProductStore.OpenDraft` (thư mục bản nháp mới khi xoá không được), `SetupForm` (báo, không văng),
  `DataFolderCheck` (cảnh báo Data trong OneDrive).
- **Hai lớp cấu hình:** `App/Hosting/StationSettings.cs` — `appsettings.json` = mặc định bản phát hành, `Data\settings.json`
  = chỉ mục trạm đổi; hỏng thì chạy mặc định + báo vàng, không ghi đè; lưu kèm `.bak`; nhật ký `settings-changed`; sao lưu
  trạm có `settings.json`.
- **Màn Cài đặt → Lưu trữ và lưu ảnh** (`UI/Settings/StorageSettingsForm.cs`, quyền `StationSettings` = Quản trị).
- **`tools/publish.ps1`** không còn xoá `Data` khi đóng gói lại cùng ngày.
- **Máy này:** `D:\MeoVision.0.261030\` + `D:\MeoVisionData\{Images,Results,Logs}`, ngưỡng dọn 3 GB (ổ D 20 GB — ngưỡng mặc
  định 30 GB sẽ xoá hết ảnh cũ). Debug: `D:\MeoVisionDev\`.
- **Dự án tham khảo:** đọc 8 dự án mới (RefVis-D…K) → `docs/BACKLOG_TU_DU_AN_THAM_KHAO.md` (phân tích vì sao + 30 việc
  làm dần, 11 dự án chưa đọc).
- Test 160 → 167. Chạy thật bản cài ổ D: `--check` sẵn sàng; Thiết lập lệnh + VisionPro editor + màn Lưu trữ mở được.

## Session 23 — 2026-10-30 — Cấu hình học từ RefVis-A/C, thanh nút mockup v2.2, sửa bố cục khung, sửa lỗi không đăng nhập được (DESIGN_LOG [26])

- **Lỗi không đăng nhập được (tác giả báo, có ảnh):** lần gọi VisionPro đầu sau khi bật máy mất ~28 s (log: hết hạn 20 s lúc
  17:30:14, VisionPro trả lời lúc 17:30:22) → bước then chốt hỏng → mọi bước sau bị BỎ QUA, kể cả nạp tài khoản → hộp
  Đăng nhập không có tài khoản nào. Sửa `UI/Program.cs`: bước "Tài khoản" chạy TRƯỚC VisionPro (chỉ cần thư mục dữ liệu),
  thời hạn bước VisionPro 20 → 60 s. Chưa chạy lại được trên exe vì lúc sửa tác giả đang mở bản Debug.
- **Vị trí file:** `DataRoot` mặc định = `Data` cạnh exe (chương trình + `.vpp` + cấu hình + tài khoản đi cùng nhau: tháo ổ
  chương trình sang máy mới là chạy). Ảnh / CSV kết quả / log có thể ra ổ khác: `ImagesRoot`, `ResultsRoot`, `LogsRoot`
  trong `appsettings.json` (rỗng = trong `Data`). Ổ cấu hình không có (máy mới chưa có ổ D:) → ghi tạm vào `Data`, bước
  "Thư mục dữ liệu" báo vàng (`AppPaths.EnsureCreated` + `FallBacks`, khoá `startup.dataFolder.fallBack`) — trước đây
  `Directory.CreateDirectory` ném lỗi làm sập chương trình trước cả khi có log. `release.json` không băm `<exe>\Data\`.
- **Cấu hình mới (product.json):** `display.slot` (lệnh cùng số khung dùng chung một khung), `extraCameras[]` (camera thêm
  → terminal ảnh của job, mỗi camera có hiệu chuẩn/exposure/brightness/contrast riêng; làn mọi camera giữ theo thứ tự tên),
  `brightness`/`contrast` (0–1, `OwnedBrightnessParams`/`OwnedContrastParams`), `lights[].settleMs` (0–2000). Bộ nạp kiểm
  tất cả (`product.cmd.badSlot/cameraTwice/extraTerminal/badCameraParam/badSettle`).
- **appsettings.json:** `DisplayColumns`, `FullScreen`, `Results.SaveAnnotatedImages` (ảnh có đồ hoạ `<tên>_view.jpg`, vẽ
  bằng `CogRecordDisplay` ẩn trên luồng STA riêng — `Vision/Storage/AnnotatedImageRenderer.cs`), `Results.ImageNamePattern`
  (`{day}/{code}/{time}_{serial}_{status}`, có `{product}`, `{channel}`). Bộ đếm giữ qua lần mở máy (`Data\counters.json`,
  đặt lại có ghi nhật ký). Ký tự kết thúc theo kênh (`terminator`) đã có từ Ch21 — bảng [26] ghi nhầm "cố định CRLF".
- **Giao diện:** thanh nút theo mockup v2.2 (Trang chính · Camera & đèn · Hiệu chuẩn · Thiết lập lệnh · Giao tiếp · Sản
  phẩm · Lịch sử · Cài đặt | Đăng nhập · Thoát; biểu tượng GDI+ `ToolbarIcons.cs`, khoá = mờ + ổ khoá + tooltip vai trò,
  rộng theo chữ, `&` không bị nuốt), chọn ngôn ngữ, ONLINE hai dòng. Form mới **Sản phẩm** (`UI/Products/ProductForm.cs`)
  và **Camera & đèn** (`UI/Devices/DevicesForm.cs`, chỉ khi OFFLINE); Hiệu chuẩn/Giao tiếp báo "đang xây".
- **Bố cục:** form theo vùng làm việc của màn hình (`ScreenFit.cs`: MainForm phóng to / FullScreen, form con co theo màn);
  lưới khung không còn ô trống (`App/Commands/FrameLayout.cs`: ≤3 khung một hàng); thanh chia Nhật ký đặt sau khi phóng to.
- **Nền ảnh xám — Session 22 chưa đúng:** đo bằng spike, `CogRecordsDisplay` đặt lại nền xanh đậm (10,10,100) MỖI lần đổi
  record. `RecordView.Show/Clear` đặt lại `BackColor` sau mỗi record; chụp exe xác nhận nền xám quanh ảnh.
- Form ẩn vẽ ảnh đồ hoạ: `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW` (không cướp focus, không có trong Alt+Tab).
- Test: 140 → **160** (FrameLayout, CounterStore, ProductCatalog, AppPaths ổ khác + ổ không có, release bỏ `Data`, bộ nạp
  nhiều camera/brightness/settle/slot, engine 2 camera + chờ đèn, mẫu tên ảnh + ảnh `_view`, ảnh đồ hoạ THẬT có điểm đỏ).
- Chạy thật exe (UIA, PrintWindow): thanh nút, form Sản phẩm, Camera & đèn (OFFLINE → chụp `100flat.bmp` 296 ms), khung
  chung T1+T2, `_view.jpg` có đường biên xanh, `counters.json`. Dữ liệu dev chép từ `%ProgramData%\MeoVision` (trừ 2 GB ảnh)
  sang `bin\Debug
et48\Data`; ProgramData giữ nguyên.

- **Sau đó (cùng ngày):** chạy lại exe Debug — log xác nhận bước Tài khoản chạy trước VisionPro, hộp Đăng nhập có tài
  khoản. Phát hành `2.0.261030` (`tools/publish.ps1`, 43 file) và **nâng cấp thật** từ `2.0.261029`: exe cũ `--backup`
  (20 file) → exe mới `--restore` → chép `audit`, `results` → `--check` "SẴN SÀNG" (exit 1: cảnh báo đèn không nối,
  file `T3_blobs - Copy.vpp` thừa). `%ProgramData%\MeoVision` giữ nguyên cho bản cũ. **2 GB ảnh** = 1003 BMP NG của vòng
  chạy thử Ch23 ngày 28/10 (23:10–23:28) → chuyển vào Thùng rác (cùng 62 MB ảnh thử hôm nay). Sách: Ch17, Ch24 sửa theo bố
  cục mới (sách Session 38), xuất bản v1.0.1.261030.

## Session 22 — 2026-10-30 — Vùng hiển thị ảnh nền xám sáng (theo ý tác giả)

- Tác giả: "phần hiển thị ảnh để tối hơi khó nhìn, cứ để hơi xám bình thường". Nền mặc định của control VisionPro
  (`CogDisplay`) là xanh rất đậm. `UI/Display/RecordView.cs`: `Background` = xám trung tính (200,200,200) +
  `UseLightBackground(CogDisplay)` — đặt `BackColor` khi control ActiveX đã có handle (đặt sớm hơn thì lỗi).
- Áp cho khung kết quả màn chính (`ResultFrameView`: nền khung 225, chữ đen; dòng chi tiết chữ đen khi OK, chữ trắng
  trên nền đỏ/cam khi NG/lỗi), Thiết lập lệnh, Lịch sử; lưới màn chính 240. Màu OK/NG/LỖI giữ nguyên.
- Build Release đạt (tools/publish.ps1). Chưa chụp lại màn hình: lúc sửa, tác giả đang chạy bản Debug.
  Ảnh Hình 22.2, 22.3, 23.1, 23.3, 23.4 trong sách vẫn là giao diện nền tối cũ.

## Session 21 — 2026-10-29 — Chương 24: triển khai và bảo trì (DESIGN_LOG [25])

- **Phát hành + kiểm cài đặt:** `tools/publish.ps1` (Release → `dist\MeoVision-2.0.YYMMDD\` → exe tự ghi `release.json`);
  `MeoVision.exe --check` chạy đúng chuỗi khởi động, ghi `diagnostics\check-*.txt`, mã thoát 0/1/2; bước mới "Tài khoản
  Windows" và "File chương trình" (thiếu/bị thay/lạ, khác bản VisionPro lúc build). Ngày build chỉ vào
  InformationalVersion (CS7034 nếu đưa vào AssemblyVersion — đo thật).
- **Sao lưu / khôi phục trạm:** `Maintenance/StationBackup.cs` (zip + mã băm từng file, kiểm toàn bộ trước khi ghi, chặn
  zip slip, cấu hình cũ chuyển sang `backup\station-before-restore-*`); `--backup`, `--restore`, menu Bảo trì (Quản trị).
- **Kiểm hồi quy:** `Commands/ReplayRunner.cs` (ảnh đã lưu của đúng phiên bản đang chạy, theo thứ tự thời gian, một job);
  bộ ảnh có nhãn `samples\<lệnh>\OK|NG` báo nhận nhầm / loại nhầm; kết quả vào hộp Áp dụng và nhật ký `apply`.
- **Gói chẩn đoán:** `Maintenance/DiagnosticBundle.cs` + `DiagnosticForm` (Vận hành xuất được), mặc định hẹp, không có
  users.json, đọc chung log đang mở.
- **Sửa lỗ hổng Ch23:** thư mục dữ liệu cho `BUILTIN\Users` tạo file (`icacls`) → ai cũng đặt được `recover-admin.txt`.
  Nay chỉ nhận file có chủ Administrators/SYSTEM; file khác bị xoá + nhật ký `admin-recovery-rejected`.
- **Lỗi tìm ra (đã sửa):** khoá dictionary bị camelCase; khôi phục vào thư mục rỗng tạo thư mục rỗng; nhãn bộ ảnh thử mất
  vì `image.Source` chỉ là tên file; `Environment.OSVersion` báo Windows 8 trên Windows 11; chữ ô "Kèm mọi sản phẩm" bị
  cắt; test gói chẩn đoán phụ thuộc giờ thật.
- Test 130 → **140/140**. Ghi chú: `CommandEngineTests.Commands_Of_Two_Cameras_Run_In_Parallel` (đo < 450 ms đồng hồ
  thật) hụt 1 lần khi cả bộ chạy song song, chạy riêng 3/3 đạt — test có từ Ch20, chưa sửa.

## Session 20 — 2026-10-28 — Chương 23: người dùng, nhật ký thay đổi, lưu kết quả/ảnh (DESIGN_LOG [24])

- Core: `Security/Permissions.cs`, `Storage/IImageStore.cs`; `CommandResult` + truy vết (kênh, serial, lúc nhận, sản phẩm,
  phiên bản); `ICommandRunner.ExecuteAsync` nhận serial.
- App: `Security/UserStore.cs` (PBKDF2, không khoá, file khôi phục), `Security/Session.cs` (tự đăng xuất, quyền, ghi
  vết), `Audit/AuditLog.cs` (JSONL theo tháng), `Results/ResultRecorder.cs` (CSV ngày × lệnh, ảnh NG trên luồng nền, hàng
  đợi có giới hạn, file bị khoá thì ghi sang file kế), `Results/ResultReader.cs`, `Results/StorageJanitor.cs`; phiên bản
  sản phẩm theo mã băm nội dung (`ProductBackup.ContentVersion`, `LoadedProduct.Version`, `ProductStore.Switched`).
- Vision: `Storage/CogImageStore.cs` (BMP, file tạm + đổi tên).
- UI: `LoginForm`, `InputActivityFilter`, `HistoryForm`; MainForm ổ khoá + ô người dùng + đếm ngược + cảnh báo mật khẩu
  cài sẵn/khôi phục; SetupForm khoá khi về Vận hành (giữ bản nháp), ghi vết áp dụng/quay lại; Program bước "Người dùng",
  "Lưu trữ", "Ghi nốt kết quả", dọn mỗi giờ; +55 khoá ngôn ngữ.
- **Lỗi tìm ra khi chạy thật (đã sửa):** (1) mở Lịch sử đúng lúc ghi làm mất một dòng — đọc chặn người ghi; (2) đồng hồ
  tự đăng xuất bị làm mới khi chuột lướt qua cửa sổ lúc đang dùng chương trình khác; (2b) nhật ký thay đổi cũng
  chuyển file khi bị khoá; (3) nút ONLINE mất ổ khoá sau khi đổi
  trạng thái; (4) dòng báo khoá trong Thiết lập lệnh bị đẩy khỏi hàng nút; (5) tóm tắt bộ ghi in hai lần khi tắt máy.
- Đo: ghi ảnh BMP 5 MP trung vị 32 ms / lớn nhất 115 ms; ~15 phút chạy thử → 1 003 ảnh NG, 2,0 GB.
- Test 111 → **130/130**. Dữ liệu chạy thử nằm trong `C:\ProgramData\MeoVision` (users.json mặc định, audit, results,
  images 2 GB); sản phẩm mẫu trả về đúng bản gốc.

## Session 19 — 2026-10-26 — Chương 22: giao diện sản xuất, bản nháp, chạy kèm (DESIGN_LOG [23])

- Core: `Commands/DisplayFrame.cs` (ảnh + record đếm tham chiếu: Share/ShareWith/Dispose), `JobRequest`,
  `IVisionJob.Run(image, request)`/`Save`/`EditSubject`, `JobRun.Record`, `CommandResult.Frame`,
  `Products/ProductConfig.cs` (`TunableConfig`, `CommandConfig.Tunables`).
- Vision: `CogVisionJob` đặt terminal input theo kiểu, tạo record trong khoá, `Save` file tạm + đổi tên.
- App: `CommandEngine` (tham số nhanh, record, trả khung sau `Completed`), `Commands/RecentFrames.cs` (8 ảnh/lệnh),
  `Commands/ShadowRunner.cs` (chạy kèm lệnh đã sửa, bỏ lượt khi bận/đang mở editor), `Products/DraftSession.cs`,
  `ProductStore.OpenDraft/Apply/RollBack` (sao lưu → chép → Switch, hỏng thì khôi phục), `ProductLoader.CheckTunables`,
  `AppPaths.Drafts`, `AppSettings.RecentFramesPerCommand/CreateDisplayRecords`.
- **Sửa lỗi (đo thật):** `ProductStore.SetCurrent` đóng job cũ TRONG khoá → lệnh chờ tới 542 ms lúc Áp dụng; nay
  cho nghỉ ngoài khoá (SW mỗi 3 s giữa ~1 000 lệnh: lớn nhất 236 → 118 ms). Spike: đóng job Blob đã chạy 2 000 lần có
  record 179 ms; record làm job Blob giữ thêm ~220 MB (có trần, trả khi Dispose).
- **Sửa lỗi (chụp ảnh cho sách lộ ra):** `ReasonDialog` mở ra con trỏ rơi vào danh sách khác biệt chỉ đọc → đặt con
  trỏ vào ô lý do, danh sách ra khỏi thứ tự Tab.
- **Sửa lỗi:** `ProductStore` ghi product.json PascalCase + null + BOM (từ Ch19, lần đầu chạy thật mới lộ) → camelCase,
  bỏ null, không BOM.
- UI: `MainForm` viết lại (thanh công cụ lớn, lưới khung theo lệnh, dock Nhật ký | Bộ đếm, ONLINE nhấn giữ 1 s,
  khởi động hỏng → OFFLINE), `Display/ResultFrameView.cs`, `Display/RecordView.cs`, `Setup/SetupForm.cs`,
  `Setup/JobEditorForm.cs` (CogToolBlockEditV2 trên job nháp), `Setup/ReasonDialog.cs`; tham chiếu control hiển thị
  Cognex; +98 khoá ngôn ngữ; bước tắt máy "Trả ảnh đang giữ".
- Mẫu: `MakeSampleJobs.cs` — T2/T3 có input `MinArea`; `samples/products/P-1024/product.json` có `tunables`.
- Test 95 → **111/111** (DraftTests 13: kiểm tham số nhanh, khung ảnh, vòng ảnh, bản nháp, chạy kèm, áp dụng/quay lại,
  áp dụng hỏng giữ nguyên, từ chối khi đổi sản phẩm; DraftRunTests 3 chạy thật VisionPro).
- Đo thật: record ~0,6 ms/lần (lần đầu 33 ms); hai đối tượng job song song trên cùng ảnh 2 400 cặp (4 job × 300 × có/không luồng đọc pixel), 0 sai.
- Chạy thật exe (đèn giả, 3 lệnh liên tục qua TCP, giao diện tiếng Việt, điều khiển bằng UIA/PostMessage — không
  đụng chuột thật): Thiết lập lệnh → MinArea 300 → chạy kèm Khác 43 · Bỏ lượt 0 → Áp dụng giữa dòng lệnh → Quay lại
  (product.json trùng bản gốc từng byte) → editor VisionPro → tắt máy đúng thứ tự. Dữ liệu ProgramData sao lưu trước
  ở scratchpad `ch22/backup_programdata`; job/product mẫu ở ProgramData đã cập nhật theo mẫu mới.

## Session 18 — 2026-10-26 — Chương 21: giao tiếp với máy (DESIGN_LOG [22])

- Core: `Commands/ICommandRunner.cs` (CommandEngine cài), `Calibration/CalibrationConfig.cs`.
- App: `Comm/MessageProtocol.cs` (tách/ghép bản tin, số trường cố định), `Comm/CommandService.cs` (khớp nguyên mã,
  mã sản phẩm, kênh, OFFLINE, HB, SW, lệnh hiệu chuẩn, lý do lỗi), `Comm/TcpChannel.cs`, `Comm/ChannelHost.cs`,
  `Comm/Plc/McProtocolClient.cs` (MC 3E nhị phân), `Comm/Plc/PlcChannel.cs` (bắt tay tên NDM),
  `Calibration/CalibrationService.cs` (CS/CP/CE); `MachineConfigLoader` kiểm kênh theo kiểu.
- Vision: `Commands/CogNPointCalibrationBuilder.cs`.
- UI: đăng ký dịch vụ, bước khởi động "Kênh giao tiếp", tắt máy ngừng nhận lệnh trước; +8 khoá ngôn ngữ;
  `machine.plc.example.json`.
- Test 74 → **95/95** (CommandServiceTests 11, ChannelTests 6 với server MC giả, CalibrationFlowTests 3 chạy thật,
  PLC machine example 1). Phát hiện: 4 điểm hiệu chuẩn có thể giấu một điểm sai sau RMS 0,000.
- Chạy thật `MeoVision.exe` với dữ liệu ProgramData (sao lưu trước ở scratchpad), đèn giả cổng 5101: 9 loại bản tin
  trả lời đúng; T2 ×50 trung vị 6,9 ms, lớn nhất 72,5 ms; tắt máy đúng thứ tự.

## Session 17 — 2026-10-25 — Giao diện v2.2 theo tình huống sử dụng (DESIGN_LOG [20])

- **Tác giả chốt (DESIGN_LOG [21]):** cho sửa khi ONLINE → thêm **chạy kèm bản nháp trên sản phẩm thật** (dải đếm
  giống/khác, ảnh khác đưa vào bộ ảnh thử), **Quay lại bản trước**, áp dụng không bắt buộc bộ ảnh thử; **bỏ** kiểm mẫu
  chuẩn đầu ca; ảnh gần nhất chỉ 8 ảnh/lệnh trong RAM, còn lại đọc từ ảnh đã lưu. Mockup, `UI_Spec_v2.md`,
  `UX_Workflows_v2.2.md` cập nhật; 384 trạng thái render không lỗi.

- `docs/ui/UX_Workflows_v2.2.md` mới: 10 tình huống (lắp máy, tạo lệnh, thử đèn/phơi sáng, đầu ca, chỉnh NG giả khi đang
  chạy, dạy lại mẫu, hiệu chuẩn lại, khiếu nại, sự cố giao tiếp, mất thiết bị), 8 cụm chức năng đi cùng nhau, 10 quyết định.
  Nguồn: đọc mã 10 repo GitHub + tài liệu In-Sight EasyBuilder/Filmstrip, MERLIC Frontend, pylon Viewer.
- Mockup `meovision_ui_mockup_v2.html` → v2.2: không gian **Thiết lập lệnh** (bảng lệnh + từng lệnh; nguồn ảnh chung; Ảnh &
  đèn có chỉ số, histogram, chụp loạt 3×3; Job có tham số nhanh + editor đầy đủ; Kết quả & giới hạn có phân bố giá trị;
  Bộ ảnh thử; thanh bản nháp + Áp dụng giữa hai lệnh); **Camera & đèn** ở cấp máy (live + đèn cùng màn, độ nét, lưới căn
  tâm); đường tắt ⚙/dải kết quả/dòng NG/ô thiết bị/ô kênh; lịch sử ghi bản sản phẩm; kiểm mẫu chuẩn đầu ca (đề xuất);
  tình huống demo "Có bản nháp". Bản 2.1 giữ ở `meovision_ui_mockup_v2.1.html`. Kiểm: 507 trạng thái render không lỗi.
- `docs/ui/UI_Spec_v2.md` → v2.2 (danh mục form, quyền, quy tắc 2/9/10, mục còn mở).

## Session 16 — 2026-10-25 — Chương 20: bộ máy chạy lệnh (DESIGN_LOG [19])

- Core: `Commands/IVisionJob.cs` (IVisionJob, JobRunStatus, JobRun), `ICalibration.cs` (+ICalibrationLoader,
  CalibrationLoad), `CommandResult.cs` (CommandStatus OK/NG/Error, OutputValue, StepTimes), `JobContract.cs`;
  `JobInspection.Job`; `DeviceException.Transient`.
- Vision: `Commands/CogVisionJob.cs` (khoá theo job, xoá output trước Run, bỏ tham chiếu ảnh sau Run),
  `Commands/CogCalibrationLoader.cs` (+CogCalibration: phép biến đổi lấy một lần, AddSpace "@\Calibrated");
  `CogJobInspector` giữ job đã mở; `CognexGigECamera` báo khung hụt là lỗi lẻ; csproj + CalibFix.
- App: `Products/LoadedProduct.cs` (lease, Retire), `ProductLoader` (giữ/đóng job, nạp hiệu chuẩn, kiểu output
  gửi được), `ProductStore` (Acquire, Close, sản phẩm cũ Retire khi chuyển), `Commands/ResultJudge.cs`,
  `Commands/CommandEngine.cs` (làn theo camera, thời hạn tính từ lúc nhận, chụp lại khi lỗi lẻ, đo từng bước);
  `DeviceManager` không đánh dấu hỏng khi lỗi lẻ.
- UI: đăng ký `ICalibrationLoader` + `CommandEngine`; bước tắt máy `shutdown.step.closeProduct`; ngôn ngữ +19 khoá.
- Mẫu: lệnh `T2` (`T2_spot.vpp`, hiệu chuẩn `TopCal`), `samples/machine/calibration/TopCal/calibration.vpp`;
  `MakeSampleJobs.cs` tạo thêm T2 + hiệu chuẩn (T1/T3 giữ nguyên file cũ).
- Phát hiện thật (spike, lưu ở dự án sách): output cũ kèm Accept; một ToolBlock hai luồng sai 9/200 không exception;
  không huỷ được ToolBlock; `new CogImage8Grey(w,h)` không xoá pixel; ảnh đều màu làm Blob lỗi ngưỡng động.
- Test 54 → **74/74** (CommandEngineTests 8, ResultJudgeTests 5, ProductTests +2, CommandRunTests 5 chạy thật).

## Session 15 — 2026-10-19 — Giá trị cấu hình viết tiếng Anh

Tác giả: nội dung trong file cấu hình nên để tiếng Anh (tránh lỗi mã hoá, định dạng, font); tên hàm tiếng Anh.

- `src/MeoVision.UI/machine.example.json`: tên kênh "Upper station" / "Lower station".
- `samples/products/P-1024/product.json`: tên sản phẩm/lệnh/output tiếng Anh ("Test plate (sample)",
  "Mean brightness"/"Brightness", "Spot count"/"Spots").
- `tests/MeoVision.App.Tests/ProductTests.cs`: JSON test tiếng Anh. `MachineConfig.cs`: ví dụ trong XML doc.
- `CogJobInspector.cs`: chú thích bỏ bí danh nguồn tham khảo (sẽ lọt vào sách).
- `CLAUDE.md`: quy tắc ngôn ngữ — tiếng Việt chỉ ở `languages/vi-VN.json`, comment và XML doc.
- Build 0 lỗi, test 54/54.

## Session 14 — 2026-10-17 — Chương 19: sản phẩm + bảng lệnh (DESIGN_LOG [18])

- Core: `Products/ProductConfig.cs` (ProductConfig, CommandConfig có `channel`, LightSetting, DisplayRecords,
  OutputConfig), `Products/IJobInspector.cs`; `MachineConfig.Channels` + `ChannelConfig`;
  `ILightController.MaxIntensity`.
- App: `Products/ProductLoader.cs` (+ MachineView; kiểm toàn bộ, gom mọi lỗi, cảnh báo .vpp thừa),
  `ProductBackup.cs` (sao lưu theo SHA-256, khử trùng lặp, Restore kiểm băm trước, Prune), `ProductStore.cs`
  (Switch kiểm xong mới đổi, Save sao lưu trước, log đủ mọi lỗi), `ProductStartup.cs`; `Devices/MachineContext.cs`;
  `MachineConfigLoader` kiểm kênh; `DeviceManager.LightLimits`; `DeviceStartup` điền MachineContext + đếm kênh.
- Vision: `Products/CogJobInspector.cs` (mở .vpp thật: ToolBlock?, terminal + kiểu, Compile Error),
  `Products/ScriptDialogGuard.cs` — **phát hiện thật**: `LoadObjectFromFile` gặp script hỏng bật MessageBox modal
  "Unable to load script" và treo luồng gọi; lượt test đầu treo 400 s; bộ canh tự đóng bằng WM_CLOSE.
- UI: đăng ký dịch vụ + bước khởi động "Sản phẩm" (then chốt); `machine.example.json` thêm `channels`; ngôn ngữ
  vi/en/zh +46 khoá, `startup.machine.ok` thêm số kênh.
- Mẫu: `samples/products/P-1024` (product.json + T1_mean.vpp, T3_blobs.vpp tạo bằng `MakeSampleJobs.cs`; chạy
  thật trên blobs.bmp: Mean 130,98 / Count 29). Phát hiện phụ: tham chiếu mặc định của script chỉ gồm assembly
  của tool đang có trong block.
- Test 39 → **54/54** (`ProductTests` 9, `JobInspectorTests` 5, `SampleProductTests` 1). Chạy app thật: P-1024
  hợp lệ + cảnh báo file thừa; P-2048 4 lỗi → Failed sau 1,7 s, không treo, log đủ 4 dòng.

## Session 13 — 2026-10-16/17 — Chốt 3 điểm còn mở; kênh giao tiếp; màn khởi động (DESIGN_LOG [17])

- Tác giả chốt: Vận hành **không** chuyển sản phẩm (chỉ Kỹ sư hoặc lệnh `SW`); PLC **Mitsubishi MC 3E (SLMP)**
  làm trước, Modbus TCP sau; giao tiếp theo **kênh** (phương án C của [17]).
- Khảo sát trước khi chốt kênh: RefVis-A (mã nguồn: 1 server cổng 3000 cho 4 camera, mỗi lệnh một luồng),
  RefVis-C (`main.ini`/`config.ini`: 8 server 10001–10008, lệnh gán `ServerIndex`), QuickBuild (đọc mẫu TCPIP
  qua API: thiết bị TCP cấp ứng dụng, 2 job chung cổng 5001), 2 repo GitHub có PLC.
- Mockup (cùng URL): form Giao tiếp tab "Kênh" (bảng kênh, cấu hình kênh đang chọn, vùng D/M + bắt tay theo
  kênh, bảng địa chỉ PLC CH1/CH2, thử gửi chọn kênh, lỗi `ERR,CHANNEL`); bảng lệnh thêm cột Kênh; thanh trạng
  thái mỗi kênh một ô; nhật ký ghi kênh; quyền chuyển sản phẩm; PLC Mitsubishi; **màn Khởi động** 7 bước (5 bước
  đã có trong code, chữ lấy từ `vi-VN.json`).
- `UI_Spec_v2.md`: §4 thêm Khởi động, §5 viết lại theo kênh + vùng PLC, §6 quyền, §9 cả 3 điểm đã chốt.

## Session 12 — 2026-10-16 — Giao diện v2.1: khớp control VisionPro (DESIGN_LOG [16])

- Tham khảo thêm nguồn của chính Cognex: sinh ứng dụng bằng Application Wizard (bản dòng lệnh) từ mẫu
  `QuickBuild2Jobs.vpw`, chạy Run Once; chạy QuickBuild; đọc mẫu `samples/Programming`; xem cách repo GitHub
  nhúng `CogToolBlockEditV2` / `CogAcqFifoEditV2` / `CogRecordDisplay`.
- Spike `docs/ui/spike/UiSpike.cs` (+ ảnh): nhúng control thật đúng kích thước mockup. Phát hiện: combobox record
  tên thô + thanh cuộn (tắt được); nền navy cố định; editor có 5 nút nguy hiểm (ẩn được); thừa hưởng font làm
  chồng chữ (đặt lại font mặc định); nạp job 14–18 s; NDM cần card CC24.
- Mockup (cùng URL) lên v2.1: khung ảnh nền navy + nút Ảnh kết quả/Ảnh gốc + combobox record cho Kỹ sư; dock 380
  thu gọn được; tab Job vẽ theo `CogToolBlockEditV2` thật; form Camera nhúng `CogAcqFifoEditV2` + "Ghi vào
  lệnh"; Hiệu chuẩn thêm tab Editor VisionPro; Giao tiếp: tín hiệu PLC theo NDM + dòng card CC24; Cài đặt: vẽ ảnh
  khi chạy, màn chờ nạp job, tỉ lệ màn hình; deep-link `#màn-vai_trò-tab`.
- `UI_Spec_v2.md` → v2.1 (§8 control VisionPro + PLC/NDM); `DESIGN_LOG.md` [16].

## Session 11 — 2026-10-16 — Giao diện v2 theo phần mềm vision chạy thật (DESIGN_LOG [15])

- Theo góp ý tác giả: bỏ gốc AM.AutoFrame; WinForms; vision là phần mềm riêng (PLC / PC control một hoặc hai
  máy tính); 3 vai trò Vận hành / Kỹ sư / Quản trị.
- Nghiên cứu: chạy thật RefVis-A và RefVis-C (chụp màn chính + 16 / 41 form), clone 11 repo vision GitHub và
  quét toàn bộ form. Ảnh + kiểm kê để ở ghi chú riêng của dự án sách (không commit).
- `docs/ui/UI_Spec_v2.md`: khung cửa sổ (thanh công cụ, lưới khung ảnh, dock Nhật ký/Chạy tay/Bộ đếm, thanh
  trạng thái), 8 form, 3 kiểu kết nối + bản tin, ma trận quyền 3 vai trò, 8 quy tắc tương tác, 3 điểm còn mở.
- `docs/ui/meovision_ui_mockup_v2.html`: mockup tương tác (đổi màn, vai trò, ngôn ngữ vi/en/zh, kiểu kết nối,
  tình huống NG / mất camera, ONLINE nhấn giữ), ghi chú "thấy ở phần mềm thật" + "quyết định" cho từng màn.
- `DESIGN_LOG.md` [15] (điều hướng, vai trò, giao tiếp, chỗ đặt cài đặt, phần thêm); [14] ghi "đã thay bằng [15]".
  v1 giữ lại để đối chiếu.

## Session 10 — 2026-10-16 — Thiết kế giao diện trước (DESIGN_LOG [14])

- Nghiên cứu: chuẩn HMI AM.AutoFrame (v3), RefVis-A (21 form), RefVis-B (sách hướng dẫn phần mềm vision
  thương mại — chương 3–4, ghi chú riêng ở sách `docs/private`), RefVis-C, Application Wizard của
  VisionPro (tài liệu cài trên máy), GitHub: RefVis-N, dự án VisionPro + Hikvision, RefVis-O, RefVis-P.
- `docs/ui/UI_Spec_v1.md`: 8 màn, khung 4 vùng, ma trận quyền 4 vai trò, 9 quy tắc tương tác.
- `docs/ui/meovision_ui_mockup_v1.html`: mockup tương tác 1920×1080 (đổi vai trò, Online/Offline nhấn giữ,
  tình huống cảnh báo/NG liên tiếp/ca mới), ghi chú thiết kế + nguồn từng màn.
- Chờ chốt: WinForms (đề xuất) hay WPF; màn riêng hay chung; ai được chuyển Online/Offline.

## Session 9 — 2026-10-16 — Lớp thiết bị (DESIGN_LOG [13]) + Chương 18 sách

- Core: `MachineConfig`/`DeviceConfig`, `IDevice`/`ICamera`/`ILightController`/`IHealthCheck`,
  `CameraImage`, `CaptureSettings`, `IDeviceFactory`, `DeviceException`, `SettingsReader`, `Guard.NotNull`.
- App: `MachineConfigLoader` (MissingMemberHandling.Error), `DeviceManager` (thời hạn, cổng, Faulted,
  watchdog backoff 2 s→60 s, health check, log stack trace chỉ lần đầu), `DeviceStartup`, đèn
  `AsciiLightController` + `ILineChannel` (Serial/TCP).
- Vision: `CogCameraImage`, `CognexGigECamera`, `FolderCamera` (CogImageFile), `RawImageConverter`,
  `CameraFactories`, **`VisionProLock`** (khoá lần chạm VisionPro đầu tiên — phát hiện bằng test chập
  chờn: song song → ArgumentNullException "pUnk" 7/40 hoặc chết testhost).
- UI: menu Thiết bị → Chụp thử, ô trạng thái thiết bị, sự kiện đổi trạng thái; `machine.example.json`;
  khoá ngôn ngữ mới (vi/en/zh, 80+ khoá); danh sách sự kiện tự cuộn.
- Test: 29 App + 10 Vision = **39/39** (Vision tắt chạy song song). Chạy app thật: đèn TCP không có bộ điều
  khiển → cảnh báo, thử lại +2 s/+4 s, bật bộ điều khiển giả → Connected; tắt máy gửi `SA0000#…SD0000#`.
- Bẫy gặp khi viết: heredoc bash rút `\\` thành `\` (file JSON mẫu sai `\b`) — viết nội dung có gạch chéo
  ngược bằng Write/Edit.

## Session 8 — 2026-10-15 — Đa ngôn ngữ + tách máy/sản phẩm (DESIGN_LOG [11]), RunMarker ([12]), Chương 17 sách

- Đa ngôn ngữ: `Msg` + `ILocalizer` (Core), `JsonLocalizer` + `LanguageFileCheck` (App), `UiText` +
  `languages/{vi-VN,en-US,zh-CN}.json` (UI); log và file sự cố ở ngôn ngữ cố định `LogLanguage`;
  `MainForm` lưu `Msg` và dựng lại chữ khi đổi ngôn ngữ; ngôn ngữ chọn được nhớ trong `ui-language.txt`.
- Bố cục dữ liệu: `AppPaths` thêm machine/products/…; `ProductPaths` cho thư mục sản phẩm tự chứa.
- **Sửa lỗi thiết kế phát hiện khi chạy thật:** `SingleInstanceGuard.PreviousInstanceCrashed` (dựa trên
  `AbandonedMutexException`) không bao giờ đúng khi kill tiến trình duy nhất — bỏ; thêm `RunMarker`
  (file `running.marker`, xoá ở bước cuối tắt máy) + test. DESIGN_LOG [12].
- `CrashReporter`: sửa chú thích sai về `UnobservedTaskException` (.NET 4.5+ mặc định nuốt lỗi, không
  sập). Log trạng thái bước bằng `ToString()` (bỏ ngoặc kép JSON). Ngắt các dòng > 95 ký tự và dòng trống
  trong `///` để trích vào sách. `Directory.Build.props` xuống dòng.
- Kiểm chứng: build 0 lỗi/0 cảnh báo; **22/22 test**; exe thật: bản thứ hai bị chặn, kill → lần sau log
  `[WRN] Previous run ended abnormally`, đóng thường → marker bị xoá; đổi ngôn ngữ vi → zh khi chạy (chụp
  bằng PrintWindow — Hình 17.2 sách).
- Sách: Chương 17 viết xong và xuất bản (code tiêm nguyên văn từ repo này); Ch14 §14.6.2 viết lại.
- PROJECT_STATUS/README viết lại theo cấu trúc mới; bản cũ ở `legacy/*_V0-V3.md`.

## Session 7 — 2026-10-14 — Dựng lại MeoVision (DESIGN_LOG [9], [10]) + code Chương 17

- Tác giả chọn: Phần VI sách 8 chương, xây thật trong MeoVision, kiến trúc lai ([9]); sau đó yêu cầu
  bỏ bản giả lập, dựng lại cấu trúc ([10]). Mã V0–V3 → `legacy/`; zip `docs/backups/2026-10-14_truoc_PhanVI.zip`.
- Solution mới: Core / Vision / App / UI + 2 project test. `Directory.Build.props`: x64, TreatWarningsAsErrors,
  polyfill `IsExternalInit` dùng chung (`build/`).
- Code Ch17: `AppPaths`, `DataFolderCheck`, `StartupSequence` (timeout kể cả bước không nghe token),
  `ShutdownSequence`, `SingleInstanceGuard`, `CrashReporter`, `VisionRuntimeCheck`, `CognexVisionRuntime`
  (CogMisc.VisionProReleaseNumber/GetLicensedFeatures — tên "Vx..."; Disconnect(false) khi thoát), `MainForm`
  (code-only), `Program` (vòng đời 5 bước), Serilog giới hạn số file/dung lượng.
- Kiểm chứng: build 0 lỗi 0 warning; 14/14 test (3 test chạy thật VisionPro 9.0 CR2); chạy exe thật:
  khởi động OK (lần gọi VisionPro đầu ~4.5 s), bản thứ hai bị chặn, nút X hỏi xác nhận (No giữ chạy, Yes
  tắt qua ShutdownSequence), đóng từ ngoài (WM_CLOSE = TaskManagerClosing) không hỏi.
- CÒN LẠI: viết Chương 17 sách; cập nhật PROJECT_STATUS/README; Ch18–24; git init + repo GitHub riêng (hỏi
  tên/public trước); sửa sách Ch14 §14.6.2 ("chạy không cần camera").

## Session 6 — 2026-07-25 — Hợp nhất design-notes; TCP comm + PLC test receiver; rà theo 10 điểm checklist vision

**Việc 4 — 4 màn hình UI ưu tiên cao + UserLevel tối giản (xong, DESIGN_LOG [8]).** Tác giả: "Xem
các phần mềm tham khảo xem phần mềm cần bổ sung các form/giao diện nào thì bổ sung cho tôi." Giao
Agent đọc lại RefVis-A, đếm chính xác 21 form/UserControl (không phải "~35+" ước lượng cũ ở §1),
nhóm 18 chức năng, đối chiếu MeoVision.UI hiện có (chỉ `FormMain`, 3 tab) + các chương sách đã
viết → Review mục 14. Chọn xây 4 nhóm **Cao** — tiêu chí: (a) một chương sách ĐÃ thiết kế/viết
code cho khái niệm đó nhưng thiếu UI ("khép kín" ví dụ dở dang), (b) cần thật cho 1 trạm đơn giản
1 camera 1 sản phẩm, không chỉ multi-station.

- **`FormTeach`** (Ch14 §14.4.2) — chọn vùng train bằng kéo chuột trên `PictureBox`
  (`SizeMode=Zoom` có viền đệm, tự quy đổi toạ độ control↔ảnh), gọi `IVisionEngine.TeachPatternAsync`.
  KHÔNG nhúng full ToolBlock editor như RefVis-A — đúng lý do ranh giới `IVisionEngine` tồn tại.
- **`FormNgImages`** (Ch14 §14.5) — lọc ảnh NG theo ngày + xem trước, đọc lại
  `InspectionLogEntry.SavedImagePath` qua `IInspectionResultStore` đã có (không thêm API mới trên
  `INgImageArchiver`). RefVis-A không có màn này (chỉ mở Explorer).
- **Tab "Xu hướng"** (Ch16 §16.4) — rolling window 30 chu trình gần nhất, biểu đồ tự vẽ GDI+ trên
  `Panel.Paint` (không thêm thư viện chart ngoài, đúng tinh thần "không cần Cpk/control chart phức
  tạp" §16.4.2), có đường ngưỡng `MinAcceptScore` của recipe. Thêm `ProcessingTimeMs` vào
  `InspectionLogEntry` (lấy từ `VisionResult` đã có sẵn) để tính cả trung bình thời gian xử lý.
- **`FormImageRetentionSettings`** (Ch14 §14.5.2) — sửa `NgImageRetentionDays`/`NgImageMaxBytes`,
  ghi thẳng `appsettings.json` qua `JObject` (giữ nguyên mọi section khác), báo rõ "áp dụng từ lần
  chạy sau" (không live-reload — `FileSystemNgImageArchiver` đã chụp giá trị cũ vào field readonly).
- **`UserLevel`/`ICurrentUserService`** (`MeoVision.Application.Auth`, mới, không nằm trong đề
  xuất ban đầu) — Ch14 §14.4.2 viết rõ Teach "chỉ Engineer trở lên" nhưng `UserLevel` **chưa hề
  tồn tại** trong code (grep xác nhận) — xây Teach mà không gác cửa thì trái thiết kế đã in. Chỉ 2
  cấp (không phải 5 như AM.AutoFrame), 1 mật khẩu Engineer đọc từ config, phiên sống trong bộ nhớ,
  KHÔNG có `IUserService`/database user đầy đủ. `FormLogin` là dialog mật khẩu tối giản.
  `FormMain` tab Cấu hình thêm nhóm "Công cụ kỹ sư" (nút Đăng nhập/xuất, Dạy mẫu, Ảnh NG, Cấu hình
  lưu ảnh — 2 nút sau chỉ bật khi Engineer).

**Không làm** (ghi rõ, không phải quên): nhóm A đầy đủ (RefVis-A: đổi mật khẩu theo ngày), nhóm E
(CRUD recipe nhiều bước), F (calibration UI có di chuyển trục), H (form IP/port PLC/MES) — xếp
Trung bình, để đợt sau.

**Lỗi build gặp phải:** `Math.Clamp` không tồn tại trên net48 (chỉ .NET Core 2.0+/.NET Standard
2.1+) — sửa bằng `Math.Min(Math.Max(x, min), max)` ở 3 chỗ (cùng loại gotcha `ArgumentNullException.ThrowIfNull`
đã gặp trước đây).

**Kiểm chứng thật:** build MSBuild 9 project, 0 lỗi 0 warning. 21/21 unit test pass — 15 test cũ +
6 test mới `CurrentUserServiceTests` (đăng nhập đúng/sai mật khẩu, đăng xuất, sự kiện `LevelChanged`).
Chạy thật `MeoVision.UI.exe` — sống > 2.5s, tiêu đề đúng. Gắn tạm nhánh `--smoketest` vào
`Program.cs` (đã xoá sau khi dùng) tự dựng cả 5 form qua DI thật + gọi `CreateControl()` (chạy
`InitializeComponent()` + tạo handle Win32 thật không cần hiển thị cửa sổ) — cả 5 `OK`. **Giới hạn
trung thực:** vẫn chưa xác minh trực quan bằng mắt (computer-use không resolve được exe tự build).

**Tài liệu:** `docs/design-notes/DESIGN_LOG.md` mục [8] (mới). `Review_RefVis-A.md` mục 14 (mới,
kiểm kê 21 form). `PROJECT_STATUS.md` cập nhật trạng thái V1 + "Việc còn lại".

Tác giả gửi danh sách 10 thành phần chuẩn của phần mềm vision công nghiệp (Image Acquisition;
Pre-processing/Calibration; Vision Tool Library; Job/Recipe Management; Decision Logic/SPC;
Communication Layer; HMI; Data Logging/Traceability; Runtime/Deployment; Diagnostics) yêu cầu tiếp
tục xây dựng theo đó; đồng thời yêu cầu 3 việc khác: (1) gộp `docs/design-notes/000N-*.md` thành
1 file duy nhất, ghi thêm vào thay vì tách file mới; (2) chọn TCP qua `127.0.0.1` làm phương tiện
truyền PLC thật (giải quyết câu hỏi còn treo ở Session 5) + xây phần mềm nhận/kiểm tra riêng; (3)
xem lại RefVis-A lần nữa để xây "dự án hoàn chỉnh".

**Roadmap hoàn thiện theo checklist 10 thành phần (mới).** Tạo `docs/ROADMAP_HOAN_THIEN.md`:
bảng đối chiếu 10 thành phần chuẩn ↔ hiện trạng MeoVision sau V0–V3 (✅/🟡/❌ + khoảng trống),
lộ trình V4–V8 (V4 TCP PLC + test receiver; V5 recipe versioning/backup/restore; V6 SQLite store
+ lưu ảnh OK theo tỷ lệ + retention + SPC/Cpk; V7 giám sát kết nối camera/PLC + reconnect backoff
+ soak test 24/7; V8 calibration pixel→mm + Teach UI + đóng gói Runtime), mỗi giai đoạn có yêu
cầu mã chi tiết/phương án kèm đánh đổi/kiểm thử/DoD/phụ thuộc; §4 gộp 15 yêu cầu chất lượng mã
áp dụng mọi giai đoạn; §5 ma trận phụ thuộc phần cứng/license/việc-của-tác-giả (`.vpp`, camera
GigE, Runtime license). Cập nhật `PROJECT_STATUS.md` mục "Việc còn lại" #8 trỏ về roadmap.

**Việc 3 — RefVis-A xem lần 2 → quyết định recipe đa bước (DESIGN_LOG [7], xong).** Tác giả
hỏi có nên theo mô hình RefVis-A: "1 phần mềm chung nhiều máy, chỉ cấu hình + `.vpp` chứa C#
script". Đọc code thật RefVis-A (shell generic + `Products\{tên}\ToolBlock\NN_*.vpp` + INI khai
báo terminal; nhưng logic đặc thù vẫn phải hardcode 1 phần trong exe — "generic 100%" vỡ ngay
trong chính dự án đó) + cân nhắc ràng buộc hiện trường tác giả nêu (nhà máy điện tử không git,
sửa tại chỗ không build lại, lo lộ code). Phân tích: exe net48 decompile dễ ngang mang source,
logic trong vpp lại ai-có-QuickBuild-cũng-sửa-được → IP không quyết định kiến trúc; cái cần thật
là "sửa hiện trường không build". **Chốt phương án B + 4 điều chỉnh** (mục [7]): recipe đa bước
(chuỗi ToolBlock khai báo trong recipe, validate terminal lúc load), `.vpp`=vision/exe=quyết
định nhưng tham số quyết định ra config, script trong vpp = van xả thuần vision qua terminal,
backup vpp/config bằng checksum+timestamp thay git. Hệ quả: `ROADMAP_HOAN_THIEN.md` V5 viết lại
thành "Recipe đa bước + versioning/backup vpp & config" (kèm cập nhật trạng thái V4 = đã triển
khai theo [6], còn nợ kiểm thử tự động); `TerminalNames` sẽ thành mặc định thay vì hằng cứng.
Lưu ý bí danh: dự án tác giả chỉ tới lần này chính là **RefVis-A** đã có trong alias map
(đối chiếu đường dẫn thật trong `RefVis_Alias_Map.md` của dự án sách) — không phát sinh RefVis-B.

**Việc 2 — TCP `127.0.0.1` + `MeoVision.PlcTestReceiver` (xong, DESIGN_LOG [6]).** Thêm
`TcpPlcCommunication`+`TcpPlcCommunicationOptions` (`MeoVision.WorkStation.Demo`, cùng chỗ với
`SimulatedPlcCommunication` — KHÔNG đặt ở `Application` để giữ layer đó trung lập phương tiện
truyền, đúng nguyên tắc R02/R03) — ASCII line-based qua `TcpClient`/`NetworkStream`, Vision là
TCP client chủ động kết nối tới PLC/gateway (server); cancel giữa chừng dùng
`ct.Register(() => client.Close())` để buộc đóng socket (net48 `StreamReader.ReadLineAsync()`
không nhận `CancellationToken`); `ConnectAsync` timeout qua `Task.WhenAny`+`Task.Delay` (net48
`TcpClient.ConnectAsync(host,port)` cũng không nhận `CancellationToken`). Xoá hẳn
`NotImplementedPlcCommunication.cs` (hết lý do tồn tại). Thêm project mới
`MeoVision.PlcTestReceiver` (net48 console, KHÔNG reference project MeoVision nào khác kể cả
`Core` — cố tình độc lập để là công cụ kiểm tra khách quan): `TcpListener` tại `127.0.0.1:5050`
đóng vai PLC/gateway, in mọi tín hiệu nhận kèm timestamp, gõ `t`/`a`/`q` để gửi TRIGGER/ACK/thoát
thủ công. `appsettings.json` của `WorkStation.Demo` thêm section `PlcCommunication` (Transport:
Tcp|Simulated — mặc định Tcp, đổi Simulated để chạy demo độc lập không cần mở receiver).

**Kiểm chứng thật (không chỉ compile):** build MSBuild 9 project, 0 lỗi 0 warning; 15/15 test cũ
vẫn pass. Chạy THẬT 2 process riêng biệt qua script PowerShell tự động hoá (giả lập thao tác gõ
`t`/`a`): 8/8 chu trình hoàn tất đúng thứ tự TRIGGER→BUSY 1→BUSY 0→DONE 1→ACK→DONE 0, tổng kết
OK=5/NG=3 khớp hệt bản `Simulated` cũ (đổi transport không đổi hành vi nghiệp vụ). **Bài học quy
trình đáng ghi:** lần thử đầu receiver báo "Lệnh không hợp lệ" cho lệnh ĐẦU TIÊN sau mỗi lần kết
nối — debug bằng cách đọc byte thô xác nhận 3 byte lạ `EF BB BF` (BOM UTF-8 thật) đứng trước dữ
liệu, hoá ra do PowerShell's `Process.StandardInput` tự chèn BOM ngay khi CHẠM `.BaseStream` (dù
chưa gọi `.Write`) — lỗi công cụ kiểm thử, không phải lỗi `TcpPlcCommunication`. Sửa đúng: đổi
`StreamReader` đọc console từ `Encoding.ASCII` (tắt dò BOM) sang `Encoding.UTF8` với
`detectEncodingFromByteOrderMarks: true` — tự nhận diện và bóc đúng BOM nếu có, không ảnh hưởng
người gõ tay thật. Chi tiết đầy đủ ở `DESIGN_LOG.md` mục [6] phần "Kiểm chứng thật". Nhân tiện
sửa mojibake tiếng Việt trên console (`Console.OutputEncoding = UTF8` ở cả 2 exe) — phát hiện
trong lúc debug, không phải mục tiêu ban đầu nhưng cùng loại vấn đề encoding console nên sửa
luôn cho nhất quán.

**Việc 1 — Hợp nhất design-notes (xong).** Gộp `0001-meovision-architecture-decisions.md` đến
`0005-camera-lighting-communication-and-net48-runtime-proof.md` (5 file) thành
`docs/design-notes/DESIGN_LOG.md` (1 file, giữ nguyên nội dung, chia mục `[1]`-`[5]` theo timeline
gốc) + xoá 5 file cũ. Quy ước mới (ghi ngay đầu `DESIGN_LOG.md`): quyết định kiến trúc tiếp theo
thêm mục `[N] YYYY-MM-DD — Tiêu đề` vào CUỐI file này, không tạo file `NNNN-*.md` riêng nữa. Đã cập
nhật toàn bộ cross-reference trỏ tới 5 file cũ: `CLAUDE.md` (mục đọc-trước), `README.md`,
`.claude/agents/meovision-code-reviewer.md`, 3 skill (`meovision-book-sync-check`,
`meovision-engine-patterns`, `meovision-ref-insight-extractor`), `reference/Outline_To_Book_Mapping.md`,
`reference/GitHub_Reference_Projects.md`, `src/MeoVision.Core/README.md` (README này còn ghi "chưa
có code" từ lúc khởi tạo dự án — nhân tiện sửa luôn cho đúng hiện trạng V0-V3 đã chạy thật). Không
sửa nội dung `CHANGELOG.md` các session cũ (giữ nguyên tính lịch sử tại-thời-điểm-đó).

## Session 5 — 2026-07-24 — Kiểm chứng net48 bằng runtime thật + ICameraSource/IPlcCommunication

Tác giả phản hồi 2 điểm sau khi đọc callout Chương 5 của sách (bản "v4.0" của Cognex.VisionPro.dll):
(1) nghi ngờ đúng — net48 vẫn hợp lý hơn net40, muốn kiểm tra "thực tế sử dụng như thế nào"; (2)
yêu cầu thêm camera/đèn/truyền thông "thiết kế hợp lý", mô phỏng phần camera không cần cầu kỳ vì có
thể test bằng camera công nghiệp thật.

**Phần 1 — net48: kiểm chứng RUNTIME thật, không chỉ lý luận.** Xác nhận registry: máy này chạy
**.NET Framework 4.8.1** (Release=533509). Dựng THẬT `CogImage8Grey` từ `Bitmap` thật, roundtrip
`ToBitmap()`, `Dispose()` — không exception. Dựng THẬT `CogToolBlock` rỗng, đọc `Inputs`/`Outputs`
— không exception. **Bài học quy trình:** lần thử đầu báo lỗi "Constructor not found"/"PSObject
cannot be converted" — KHÔNG phải lỗi tương thích .NET, mà PowerShell tự bọc object trong `PSObject`
khi Invoke qua `Activator.CreateInstance`/`ConstructorInfo.Invoke` bằng mảng; phải unwrap
(`.PSObject.BaseObject`) trước. Suýt kết luận nhầm "không tương thích" từ lỗi công cụ kiểm tra, không
phải lỗi thật — đã tự phát hiện và sửa trước khi báo cáo. **Xác nhận: MeoVision chưa từng target
net40** — mọi `.csproj` luôn `net48` từ 0002/0003; "v4.0" chỉ là thông tin về bản thân DLL Cognex.

**Phần 2 — nghiên cứu trước khi thiết kế camera/đèn/truyền thông** (2 Explore agent song song):
- Sách Ch6: thu ảnh thật = `CogAcqFifoTool` bọc `ICogAcqFifo`, tạo qua
  `ICogFrameGrabber.CreateAcqFifo`; trigger phổ biến nhất = **Semi** (Bảng 6.2). "Đèn" trong ngữ
  cảnh vision = **strobe đồng bộ exposure** (§6.2.5: `ICogAcqStrobe.StrobeEnabled`, `StrobeDelay`,
  `StrobePulseDuration`) — không phải đèn tháp báo trạng thái.
- Sách Ch15: giao thức Trigger/Busy/Done/Ack cố tình **trung lập phương tiện truyền** — không có
  class I/O cụ thể nào trong sách.
- AM.AutoFrame: `ICameraDevice.SetLightAsync(bool)` (đèn gắn camera) tách biệt hẳn với
  `ILightController`/`TowerLightState` (đèn tháp andon R/Y/G/Buzzer) — xác nhận đây là 2 khái niệm
  khác nhau ngay trong quy ước riêng của tác giả, không phải MeoVision tự sáng tác.

**Đã hỏi tác giả 1 câu trước khi viết** (phương tiện truyền PLC thật): xác nhận **chưa chọn** — chỉ
viết interface + bản giả lập, bản thật để `NotImplementedPlcCommunication` (throw rõ ràng).

**Đã thêm (MeoVision.Core):** `CameraTriggerMode` (FreeRun/Auto/Semi/Manual), `ICameraSource`
(`ConnectAsync`/`ConfigureAsync(VisionRecipe)`/`GrabAsync():Task<Bitmap>`, tách khỏi `IVisionEngine`
đúng ranh giới "chụp" ≠ "xử lý" sách vẽ Ch1 Hình 1.2), `IPlcCommunication` (4 tín hiệu, chỉ `bool`,
không biết `VisionResult` — đúng tinh thần trung lập phương tiện của sách). `VisionRecipe` thêm
`TriggerMode`/`StrobeEnabled`/`StrobeDelayUs`/`StrobePulseDurationUs` (mặc định tắt/Semi).

**Đã thêm (MeoVision.Engine):** `CognexCameraSource` — thu ảnh GigE thật qua `CogFrameGrabberGigEs`/
`ICogFrameGrabber.CreateAcqFifo`/`ICogAcqTrigger.TriggerModel`/`ICogAcqStrobe*`. Mọi API xác nhận
bằng reflection trước khi viết, kể cả chi tiết nhỏ (`CogStringCollection` có indexer `int`, cần cho
`AvailableVideoFormats[0]`). Thêm reference `Cognex.VisionPro.FGGigE.dll` vào csproj.
`CognexVisionEngine` sửa: constructor nhận `ICameraSource?` tuỳ chọn, `LoadRecipeAsync` gọi
`ConfigureAsync` khi có camera source, `InspectAsync` tự `GrabAsync()` khi
`VisionJobRequest.SourceImage` rỗng (trước đây throw luôn — giờ mới thật sự "tự thu ảnh" như comment
cũ đã hứa nhưng chưa làm).

**Đã thêm (MeoVision.Engine.Simulated):** `SimulatedCameraSource` — CỐ TÌNH đơn giản (1 khung xám
trơn), không lặp lại logic `SyntheticPartRenderer` — đúng yêu cầu tác giả: mô phỏng camera không cần
cầu kỳ vì test bằng camera thật là chính.

**Đã thêm (MeoVision.WorkStation.Demo):** `SimulatedPlcCommunication` (in console đúng 4 tín hiệu —
narrate tín hiệu THÔ chuyển vào đây, tách khỏi nghiệp vụ vision), `NotImplementedPlcCommunication`
(stub throw `NotSupportedException`, giống tiền lệ `TeachPatternAsync`). Refactor
`PlcHandshakeSimulator` thành orchestrator gọi `IPlcCommunication` cho tín hiệu thô, chỉ tự narrate
phần Outcome/Score (nghiệp vụ vision, không phải ngữ nghĩa giao thức PLC).

**Đã thêm (MeoVision.UI):** dòng hiển thị TriggerMode/Strobe vào label recipe-detail có sẵn ở tab
Cấu hình (mở rộng control có sẵn thay vì thêm control mới — giữ thay đổi UI tối thiểu, tương xứng).

**Build/test lại toàn bộ sau khi thêm:** 8+2 project build 0 lỗi 0 warning (kể cả `CognexCameraSource`
compile-verify chống lại `Cognex.VisionPro.FGGigE.dll` thật); 15/15 unit test cũ vẫn pass (không đổi
hành vi V0); chạy lại `MeoVision.WorkStation.Demo.exe` thật sau refactor `IPlcCommunication` — 8 chu
trình, 5 OK/3 NG, đúng trình tự tín hiệu, ảnh NG thật vẫn lưu đĩa — xác nhận refactor không đổi hành
vi quan sát được. Chạy lại `MeoVision.UI.exe` — vẫn khởi động đúng sau khi sửa Cấu hình tab.

**Tài liệu:** `docs/design-notes/0005-camera-lighting-communication-and-net48-runtime-proof.md`
(mới). `CLAUDE.md` cập nhật sơ đồ solution + thứ tự đọc.

**Chưa làm (có chủ đích):** chạy `CognexCameraSource` end-to-end thật (cần camera GigE vật lý);
viết implementation thật cho `IPlcCommunication` (chờ tác giả chọn phương tiện truyền).

## Session 4 — 2026-07-24 — Viết code thật V0→V3, build/test/chạy xác nhận

Tác giả: "viết code cho dự án MeoVision, ưu tiên chương trình theo hướng giống phần mềm thực tế
nhất có thể chứ không chỉ là mẫu học tập, code có thể khác sách — khi đó điều chỉnh sách cho phù
hợp, không bắt buộc chương trình và sách giống nhau hoàn toàn."

**Quy trình:** dùng Plan agent nghiên cứu sâu (đọc lại 0001-0003 + Chương 1/13/14/15 thật của sách
+ probe reflection trực tiếp DLL Cognex cài máy) để thiết kế kế hoạch trước khi viết — plan được
trình bày tóm tắt cho tác giả, sau đó thực thi tuần tự V0→V1→V2→V3, build+test thật sau mỗi bước
thay vì viết hết rồi mới kiểm.

**V0 — MeoVision.Core + Engine.Simulated + Application + WorkStation.Demo skeleton:**
- `MeoVision.Core`: `IVisionEngine`, `VisionResult`/`VisionCheckResult`/`VisionRecipe`/
  `VisionJobRequest`/`VisionTrainRegion` (record), `VisionEngineException` + 3 lớp con
  (`VisionRecipeException`/`VisionJobLoadException`/`VisionEngineTimeoutException`),
  `Guard.ThrowIfNull` (net48 không có `ArgumentNullException.ThrowIfNull` — xác nhận thật bằng lỗi
  biên dịch CS0117 khi thử dùng trực tiếp).
- `MeoVision.Engine.Simulated`: `SimulatedVisionEngine` (Random inject được qua constructor để
  test tất định) + `SimulatedEvaluator` (đánh giá THUẦN, tách khỏi phần random — cho phép unit test
  trực tiếp không cần kiểm soát seed) + `SyntheticPartRenderer` (vẽ ảnh part+pad+defect thủ tục
  bằng `System.Drawing.Graphics`, watermark "SIMULATED").
- `MeoVision.Application`: `InspectionCycleService` (retry chỉ khi `PartNotFound`, không retry
  `MeasurementFailed` — đúng Ch15 §15.4.2), `JsonFileRecipeRepository` (Newtonsoft.Json),
  `InMemoryInspectionResultStore`, `AlarmService`, `FileSystemNgImageArchiver` (chỉ lưu
  Reject/Warning/Error — **policy nằm ở cycle service, archiver chỉ lo cơ chế** — tách ra sau khi
  nhận ra thiết kế ban đầu đặt sai chỗ khiến unit test archive-policy không test đúng lớp).
- Gặp và sửa 2 gotcha net48+C# hiện đại thật: `IsExternalInit` polyfill cho record (lỗi CS0518),
  `ArgumentNullException.ThrowIfNull` không tồn tại (lỗi CS0117) → `Guard.ThrowIfNull` tự viết.
- **Build/test thật**: `MSBuild.exe` (không phải `dotnet build` — net48 thuần cần MSBuild thật) 0
  lỗi 0 warning; `vstest.console.exe` 15/15 test pass; chạy `MeoVision.WorkStation.Demo.exe` thật →
  1 chu trình Reject thật (width 24.47mm ngoài dải 24.5-25.5mm), ảnh PNG 373×200 thật được vẽ +
  lưu đĩa (xác nhận bằng `file` command, không chỉ tin log).

**V1 — MeoVision.UI (WinForms):** `FormMain` 3 tab (Vận hành/Lịch sử/Cấu hình) viết tay theo đúng
convention Designer.cs/`.cs` tách đôi (không dùng VS Designer, nhưng giữ cấu trúc y hệt để mở được
bằng Designer sau này), `PictureBox`+GDI+ overlay, `Timer`-driven run loop (không dùng
CancellationToken thủ công — Timer.Tick tự chạy trên UI thread, đơn giản hơn cho thao tác nhịp UI),
cross-thread marshaling đúng chuẩn cho `AlarmRaised` event (`InvokeRequired`/`BeginInvoke`). Gặp 3
lỗi build thật: namespace `MeoVision.Application` (project) trùng tên `System.Windows.Forms.
Application` (lớp) → dùng tên đủ; `Application.SetHighDpiMode` không có trong bản WinForms net48
đang resolve → bỏ; CS8618/CS8602 do phân tích nullable không lần qua ranh giới 2 file Designer/
code-behind của cùng partial class → `NoWarn` ở mức project (friction point quen thuộc mọi dự án
WinForms+nullable, không phải lỗi thật). Build 0 lỗi 0 warning; chạy thử `.exe` xác nhận process
khởi động đúng tiêu đề cửa sổ (DI+InitializeComponent không exception) — **không xác minh được
trực quan bằng computer-use** (tool chỉ resolve app đã đăng ký Start Menu, không thấy exe tự build).

**V2 — MeoVision.Engine (Cognex thật):** trước khi viết, phản chiếu (reflection) trực tiếp DLL
Cognex cài máy để xác nhận API thật thay vì suy đoán từ sách/mẫu tham khảo — phát hiện đáng chú ý:
`CogToolBlock` nằm trong `Cognex.VisionPro.ToolGroup.dll` nhưng namespace thật là
`Cognex.VisionPro.ToolBlock` (không phải `.ToolGroup`); `CogToolBlockTerminal` (kiểu trả về của
`Inputs["x"]`/`Outputs["x"]`) lại nằm ở assembly khác hẳn (`Cognex.VisionPro.dll` cơ bản, không
phải `ToolGroup.dll` dù cùng namespace) — nếu không kiểm chứng sẽ viết sai `using`. Xác nhận
`ICogRunStatus` (Result/Message/ProcessingTime/Exception) khớp gần như 1-1 với `VisionOutcome`/
`VisionResult` đã thiết kế ở Core từ trước (không phải trùng hợp — kế thừa từ nghiên cứu sách).
`CognexVisionEngine` implements `IVisionEngine`, đọc terminal qua `TerminalNames` (hằng số 1 nơi
duy nhất). 1 lỗi build thật: `using ICogImage cogImage = ...` không hợp lệ (interface không tự kế
thừa `IDisposable`, chỉ class cụ thể `CogImage8Grey` có) → sửa khai báo kiểu cụ thể.
**Build thành công chống lại DLL Cognex thật cài trên máy** (GAC, xác nhận `Assembly.Load` bằng
strong name suông load được từ `GAC_64`) — đây là compile-verify thật, không phải "để sau". Chạy
end-to-end với ảnh/job thật cần tác giả tự dựng `.vpp` bằng QuickBuild (thao tác GUI, không tự
động hoá được) khớp `TerminalNames`.

**V3 — WorkStation.Demo mô phỏng bắt tay PLC:** `PlcHandshakeSimulator` narrate đúng trình tự
Trigger/Busy/Done/Ack quanh `InspectionCycleService` thật, watchdog 2 tầng độc lập (PLC rộng hơn
`CycleTimeoutMs` nội bộ Vision — đúng nguyên tắc Ch15 §15.1.3 "mỗi bên tự có đồng hồ riêng"), công
thức worst-case `(MaxRetries+1) × CycleTimeoutMs` in ra console (Ch15 §15.5.2). Chạy thật 8 chu
trình: 5 OK/3 NG, ảnh NG thật được lưu, đúng trình tự tín hiệu mỗi chu trình.

**Tài liệu:** `docs/design-notes/0004-book-follows-code-not-vice-versa.md` (mới) — đảo ngược nguyên
tắc "code phải khớp sách", làm rõ lại ranh giới Cognex (Engine=xử lý, UI=được phép nhúng hiển thị
sau này), ghi 5 điểm code-vs-sách lệch nhau cần tác giả quyết định cập nhật sách ở phiên nào.
`CLAUDE.md` cập nhật bảng nguyên tắc bất biến + sơ đồ solution (phản ánh code thật, không còn kế
hoạch) + ghi chú `Guard.ThrowIfNull`/`IsExternalInit` polyfill cho session sau khỏi lặp lại lỗi.

**Chưa làm (có chủ đích, không phải bỏ sót):** cập nhật `output/Chapter_*.md` của sách theo 5 điểm
lệch (việc của phiên làm việc dự án sách, không phải MeoVision); dựng `.vpp` thật; màn hình Teach
thật (V4+); nhúng `CogRecordDisplay` vào UI (tuỳ chọn, không phải yêu cầu).

## Session 3 — 2026-07-23 — Kiểm chứng net48 bằng 5 repo GitHub thật (git clone + đọc source)

Tác giả gửi 5 URL GitHub cụ thể (`RefVis-G` thư mục con, `RefVis-L`,
`RefVis-M`, `RefVis-E`, `RefVis-K`) kèm nhận xét: có vẻ quyết
định "net framework WinForms" của Session 2 quá hẹp, đề nghị kiểm tra lại — cho phép tải về máy để
kiểm tra mã nếu cần.

**Đã làm:** `git clone` cả 5 repo về scratchpad (2 repo đầu tải thẳng; 2 repo sau quá lớn do commit
kèm `packages/`/database — dọn sạch, tải lại bằng `--filter=blob:none` + sparse-checkout chỉ lấy
`.csproj`/`.md`/`.sln`, tránh tải nhầm hàng trăm MB binary không cần). Đọc trực tiếp mọi `.csproj`
(không suy đoán từ tên file) + `grep` API `Cog*` thật trong `.cs`/`.xaml`.

**Kết quả (tách đúng 2 trục độc lập mà nhận xét của tác giả gộp làm 1):**

1. **net48 (.NET Framework) vs .NET hiện đại** — **XÁC NHẬN LẠI, không đổi.** Cả 6 project con
   dùng Cognex thật tìm được đều target .NET Framework: `v4.6` (RefVis-E), `v4.7.2`
   (RefVis-K/VisionControl), `v4.8` (RefVis-G, RefVis-K/Plugin.MotionControl), `v4.8.1`
   (RefVis-L, RefVis-G), `net48` kiểu SDK mới (RefVis-M). 0/6 dùng Core/5+/6+/8+/9. Trải nhiều
   tác giả/công ty/năm khác nhau (Hàn Quốc, Trung Quốc) — hội tụ độc lập. Cộng với spike thật của
   AM.AutoFrame (SEHException tái lập trên .NET 9) đã có từ trước, đây là bằng chứng đủ mạnh để
   không cần xét lại trục này nữa.
2. **"Chỉ WinForms" (phần Session 2 quyết định)** — **SAI, đã sửa.** `RefVis-M`
   là bằng chứng thật: WPF + `WindowsFormsHost` bọc `CogRecordDisplay`/`CogToolBlockEditV2`/
   `CogAcqFifoEditV2` — chạy được, dùng API Cognex thật (`CogSerializer`, `CogToolBlock`...) trong
   `ViewModels/*.cs` thật, vẫn trên net48. Đúng pattern AM.AutoFrame đã dùng (2 dự án hội tụ độc
   lập cùng giải pháp kỹ thuật).

**Quyết định:** giữ nguyên WinForms cho `MeoVision.UI` (đơn giản hơn cho ví dụ sách, khớp guide
chính thức Cognex, đa số mẫu thật khảo sát được — 3/5 — cũng chọn WinForms) nhưng đây giờ là **lựa
chọn có lý do**, không phải ép buộc kỹ thuật. Đã ghi đầy đủ phân tích + bảng so sánh vào
`docs/design-notes/0003-github-verification-net48-confirmed.md` (mới, bổ sung 0002 — không sửa
0002 theo đúng quy ước ADR).

**Phát hiện phụ:** `RefVis-G (lớp hiển thị bộ đếm kết quả)` dùng
`PGgloble.getInstance` — cùng anti-pattern singleton toàn cục đã bị flag ở RefVis-A (ADR 0007 của
AM.AutoFrame) — lần thứ 2 gặp độc lập, củng cố thêm lý do MeoVision tránh pattern này.

**Đã cập nhật:** `reference/GitHub_Reference_Projects.md` (thêm 5 repo mới + bảng target framework
tổng hợp), `CLAUDE.md` (nới "WinForms" từ ép buộc thành mặc định, cập nhật thứ tự đọc thêm 0003).

## Session 2 — 2026-07-22 — Đảo ngược kiến trúc sang net48 + sửa nghiên cứu GitHub

Tác giả phản biện 2 điểm của Session 1 trong cùng một tin nhắn.

**1. Kiến trúc: net48 WinForms 1 process, KHÔNG tách process/IPC**

Tác giả: kiến trúc tách process (net48 Engine + .NET 9 Application/WPF UI + IPC) mà Session 1 chọn
"có khả năng cao là không chạy được" — nên dùng net48 WinForms nguyên khối.

- **Gốc rễ lỗi của Session 1**: copy nguyên lý do "cần tách process" từ AM.AutoFrame ADR 0008 mà
  không kiểm tra tiền đề đó có áp dụng cho MeoVision không. AM.AutoFrame cần tách vì bản thân nó
  ĐÃ cam kết .NET 9/WPF/Prism cho toàn app (vì lý do khác, không liên quan vision) — vision chỉ là
  một capability gắn thêm. MeoVision không có ràng buộc đó — nó tồn tại CHỈ để minh hoạ VisionPro,
  nên không có lý do gì để chấp nhận rủi ro/độ phức tạp IPC.
- Đã tạo `docs/design-notes/0002-revert-to-single-process-net48.md` — ADR đảo ngược đầy đủ: bảng
  so sánh A (net48 1-process, chọn) vs B (kiến trúc gốc 0001), cấu trúc solution mới (6 project
  src + 2 test, TOÀN BỘ net48; bỏ `MeoVision.Ipc`).
- Đã xoá `src/MeoVision.Ipc/` + skill `.claude/skills/meovision-ipc-contract/`.
- Đã đổi tên `MeoVision.Core.Abstractions` → `MeoVision.Core`.
- Đã cập nhật: `CLAUDE.md` (thứ tự đọc ưu tiên 0002, bảng nguyên tắc bất biến, sơ đồ solution),
  `PROJECT_STATUS.md` (Session 2, TODO rút gọn, lộ trình V0-V3 bỏ bước dựng IPC), `README.md`
  (yêu cầu chỉ còn net48 Developer Pack, bỏ .NET 9 SDK), `reference/Outline_To_Book_Mapping.md`
  (bỏ dòng `MeoVision.Ipc`, ghi chú WinForms nhúng thẳng `CogRecordDisplay`), tất cả README project
  con trong `src/`/`tests/`, skill `meovision-engine-patterns` (câu "lọt ra ngoài qua IPC" sửa
  thành "lọt ra ngoài qua return type public của `IVisionEngine`" — ranh giới giờ là trong-process).
- `docs/design-notes/0001-...md` **giữ nguyên, không sửa** (theo đúng quy ước ADR: quyết định cũ
  không bị xoá lịch sử, chỉ bị supersede bởi bản ghi mới) — Quyết định 2/3 của 0001 nay đã lỗi
  thời, đọc 0002 để biết bản hiện hành.

**2. Nghiên cứu GitHub: tìm lại bằng từ khoá API cụ thể — tìm ra kết quả thật**

Tác giả: nên tìm GitHub bằng từ khoá đặc trưng (`CogPMAlign`...) thay vì mô tả chung.

- Kết luận "không có repo public đáng kể" của Session 1 **SAI** — do tìm bằng mô tả chung. Tìm lại
  bằng tên class thật (`CogPMAlignTool`, `CogBlobTool`, `CogToolBlock`, `CogAcqFifoTool`,
  `CogCaliperTool`, `CogIDTool`, `CogFixtureTool`) qua WebSearch + xác minh bằng WebFetch trực tiếp
  vào repo → tìm ra nhiều kết quả thật, đã ghi đầy đủ vào `reference/GitHub_Reference_Projects.md`
  (mới) kèm mức độ tin cậy (đã kiểm chứng nội dung thật vs. chỉ thấy mô tả).
- Đáng chú ý nhất: **`kelvin-luo/visionProDemo`** — WinForms thật, đã đọc source `MainForm.cs` thật,
  dùng `CogToolBlock`/`CogSerializer`/`CogImageFileTool`/`ICogAcqFifo`/`CogTransform2DLinear` — khớp
  gần đúng kiến trúc MeoVision hiện tại. Cũng tìm thấy `zhy29563/VisionProExamples` (24 file `.vpp`,
  không phải source), `RefVis-G` (dự án thật, MIT license) + 3 repo cùng
  tác giả (`Latch-Vision`/`Channel-Vision`/`Tongue-Vision`, chưa fetch sâu), `CognexBarcodeReader`,
  `CognexWeintekPlayer`.
- Xác nhận KHÔNG có trang GitHub tổ chức chính thức của Cognex Corporation với repo VisionPro công
  khai (`github.com/Cognex` tồn tại nhưng là các dự án AI-agent không liên quan — khác tổ chức).
- Đã sửa `docs/design-notes/0001-...md` mục "Nghiên cứu GitHub" — đính chính kết luận sai, trỏ sang
  `reference/GitHub_Reference_Projects.md`, ghi rõ bài học quy trình (tên API cụ thể → tìm bằng
  tên literal, không dùng mô tả chung).
- Không copy code từ bất kỳ repo tìm được — chỉ tham khảo pattern (đã ghi rõ trong file mới, kèm
  giải thích vì sao trích tên thật ở đây không vi phạm quy ước ẩn danh RefVis-X: đây là repo công
  khai, khác RefVis-A là dự án nội bộ cần bảo mật).

**Còn lại:** tác giả vẫn chưa xác nhận vị trí thư mục (`C:\Users\dinhn\OneDrive\C\VS\MeoVision`) —
xem TODO #1 trong `PROJECT_STATUS.md`.

## Session 1 — 2026-07-22 — Khởi tạo dự án

Tác giả yêu cầu xây dựng chương trình xử lý ảnh làm ví dụ chi tiết cho sách VisionPro, tham khảo
các dự án sách khác, AM.AutoFrame, một dự án vision tham khảo thật, và xu hướng GitHub gần đây.

**Nghiên cứu đã làm:**
- `CSharp_Automation_Book` (Explore agent): xác nhận sách trước KHÔNG có code chạy được riêng —
  "MeoFrame" chỉ là tên minh hoạ dùng lại qua nhiều chương với kiến trúc khác nhau mỗi chương.
  MeoVision lần này khác biệt: phải chạy được thật. Tìm thấy skill `project-insight-extractor`
  (mô hình review dự án tham khảo có ẩn danh + agent bảo vệ context) — dùng làm khuôn mẫu.
- `AM.AutoFrame` (Explore agent, đọc code thật): phát hiện quan trọng nhất — ADR
  `0008-vision-process-separation.md` đã **chứng minh bằng 6 spike thật** rằng nạp `.vpp`/
  `CogSerializer` in-process trên .NET 9 gây `SEHException` native. Quyết định đã chốt ở đó: tách
  VisionPro ra process .NET Framework 4.8 riêng, IPC JSON. ADR `0007-vision-module-design.md` còn
  cho thấy chính RefVis-A (xem dưới) từng được dùng làm tài liệu tham khảo khi thiết kế
  `AM.Modules.Vision` — kèm phân tích kiến trúc chi tiết tái sử dụng được.
- RefVis-A (dự án vision C#/WinForms/Cognex thật, ẩn danh theo quy ước sách) — đối chiếu chéo với
  phân tích đã có sẵn trong ADR 0007 của AM.AutoFrame, viết lại thành
  `VisionPro_Image_Processing_Book/docs/private/project-reviews/Review_RefVis-A.md` (đã grep xác
  nhận không còn tên thật nào lọt vào).
- GitHub (WebSearch): không có repo public đáng kể tích hợp Cognex VisionPro (SDK bản quyền, hợp
  lý). Xu hướng skill/agent 2026: description = routing rule, mỗi skill một việc, code tất định
  cho việc tất định — áp dụng khi thiết kế 5 skill của MeoVision.

**Đã tạo:**
- `docs/design-notes/0001-meovision-architecture-decisions.md` — outline kiến trúc đầy đủ, trình
  bày phương án + đánh đổi cho quyết định quan trọng nhất (tách process Engine/Application).
- `CLAUDE.md`, `PROJECT_STATUS.md`, `.gitignore`.
- Skeleton thư mục 7 project (`Core.Abstractions`, `Engine`, `Engine.Simulated`, `Ipc`,
  `Application`, `UI`, `WorkStation.Demo`) + 2 test project — hiện là thư mục rỗng, chưa có
  `.csproj`/code thật (chờ xác nhận kiến trúc).
- 5 skill (`meovision-engine-patterns`, `meovision-ipc-contract`, `meovision-sim-parity`,
  `meovision-book-sync-check`, `meovision-ref-insight-extractor`) + 1 agent
  (`meovision-code-reviewer`).
- `docs/private/RefVis_Alias_Map.md` + review RefVis-A trong dự án sách (không phải MeoVision).

**Chưa làm (cố ý, đúng yêu cầu "chuẩn bị trước khi viết code"):** chưa sinh `.sln`/`.csproj` thật,
chưa viết bất kỳ logic `.cs` nào. Bước tiếp theo cần tác giả xác nhận 2 điểm trong
`PROJECT_STATUS.md` mục TODO trước khi bắt đầu V0.
