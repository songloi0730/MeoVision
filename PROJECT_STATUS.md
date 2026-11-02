# PROJECT_STATUS — MeoVision

> Đọc file này TRƯỚC NHẤT mỗi session. Trạng thái của bản V0–V3 cũ: `legacy/PROJECT_STATUS_V0-V3.md`.

**Cập nhật lần cuối:** 2026-11-01 (Session 27 — sửa treo khi để yên lâu: ghim DLL native VisionPro — DESIGN_LOG [30]; Session 26 — không gửi lý do cho máy, lệnh `kind` Inspect/Guide, bỏ qua chỉ cho lệnh kiểm tra — DESIGN_LOG [29]; trước đó Session 25: bỏ qua có kiểm soát, dải cảnh báo, Giao tiếp, Hiệu chuẩn — DESIGN_LOG [28]). MeoVision đã dựng lại từ đầu cho **Phần VI**
của sách (DESIGN_LOG [9]–[19]). Chương 17–21 đã **xong code và xong chương sách**; code
trong sách được tiêm nguyên văn từ repo này bằng `tools/inject_code.py` của dự án sách. Vì vậy **mọi
thay đổi ở các file đã trích vào sách** đều phải chạy lại script đó (xem "Ràng buộc với sách" bên dưới).

## Trạng thái tổng

| Hạng mục | Trạng thái |
|---|---|
| Nền tảng | net48 x64, C# 11 (+ polyfill `IsExternalInit`), `TreatWarningsAsErrors`, VisionPro 9.0 CR2 **bắt buộc**, không có engine giả lập (DESIGN_LOG [10]) |
| Solution | 4 project src (Core / Vision / App / UI) + 2 project test. Build 0 lỗi 0 cảnh báo |
| Test | **180/180 pass** (Vision chạy tuần tự) (`vstest.console /Platform:x64`); test tích hợp chạy THẬT VisionPro (thiết bị, mở .vpp, sản phẩm mẫu) |
| Ch17 — vòng đời | ✅ `AppPaths`/`ProductPaths` (3 vùng dữ liệu), `DataFolderCheck`, `StartupSequence`, `VisionRuntimeCheck` + `CognexVisionRuntime`, `SingleInstanceGuard`, `RunMarker`, `CrashReporter`, `ShutdownSequence`, `Program` |
| Ch17 — đa ngôn ngữ | ✅ `Msg`, `ILocalizer`, `JsonLocalizer`, `LanguageFileCheck`, `UiText`, `languages/{vi-VN,en-US,zh-CN}.json`, đổi ngôn ngữ khi đang chạy |
| Kiểm chứng chạy thật (exe) | ✅ khởi động OK (lần gọi VisionPro đầu 4,5–11 s); bản thứ hai bị chặn; kill → lần sau log "Previous run ended abnormally"; X hỏi xác nhận, `CloseMainWindow` không hỏi; đổi ngôn ngữ vi → zh khi chạy (ảnh: Hình 17.2 của sách) |
| Ch18 — thiết bị | ✅ machine.json + factory, DeviceManager (watchdog), CognexGigECamera (chỉ kiểm nhánh không có camera), FolderCamera, AsciiLight, RawImageConverter, VisionProLock — 39/39 test, app thật |
| Ch19 — sản phẩm + bảng lệnh | ✅ product.json (kênh, vai trò, job tên file, đèn, outputs), ProductLoader kiểm toàn bộ, CogJobInspector (.vpp thật) + ScriptDialogGuard (hộp thoại modal làm treo), ProductBackup theo SHA-256, ProductStore.Switch kiểm xong mới đổi — 54/54 test, app thật (DESIGN_LOG [18]) |
| Ch20 — bộ máy chạy lệnh | ✅ code: job giữ sau khi kiểm (`LoadedProduct` + lease), hiệu chuẩn `AddSpace`, `CommandEngine` (làn theo camera, thời hạn, chụp lại lỗi lẻ, đo từng bước), `ResultJudge` (OK/NG/Error, văn hoá bất biến) — 74/74 test, sản phẩm mẫu chạy thật 4–7 ms/lệnh (DESIGN_LOG [19]). Chương sách xong (2026-10-25) |
| Ch21 — giao tiếp | ✅ code: CommandService (bản tin số trường cố định, mã sản phẩm, lý do lỗi), TcpChannel, PLC MC 3E (McProtocolClient + PlcChannel, bắt tay tên NDM, server MC giả trong test), hiệu chuẩn do máy dẫn CS/CP/CE → calibration.vpp — 95/95 test, exe chạy thật qua TCP (DESIGN_LOG [22]). Chương sách xong (2026-10-26) |
| Ch22 — giao diện | ✅ code: màn chính (lưới khung theo lệnh, record VisionPro, dải 16 kết quả, Nhật ký/Bộ đếm, ONLINE giữ 1 s), Thiết lập lệnh (bản nháp, tham số nhanh, giới hạn, ảnh & đèn, bộ ảnh thử, editor VisionPro trên job nháp), chạy kèm, áp dụng/quay lại giữa hai lệnh, `DisplayFrame` đếm tham chiếu — 111/111 test, exe chạy thật (DESIGN_LOG [23]). Chương sách xong (2026-10-28) |
| Ch23 — người dùng & dữ liệu | ✅ code: 3 vai trò + bảng quyền, PBKDF2, không khoá, file khôi phục Quản trị, tự đăng xuất, nhật ký thay đổi JSONL, phiên bản sản phẩm trong từng kết quả, CSV ngày × lệnh + ảnh NG trên luồng nền, xoay vòng theo ngày/dung lượng, form Đăng nhập + Lịch sử truy ngược phiên bản — 130/130 test, exe chạy thật (DESIGN_LOG [24]); ✅ chương sách đã viết + xuất bản v1.0.1.261028 |
| Ch24 — triển khai & bảo trì | ✅ code: release.json + `tools/publish.ps1` (2.0.YYMMDD), `--check`, sao lưu/khôi phục trạm, chạy lại ảnh đã lưu + bộ ảnh có nhãn, gói chẩn đoán, sửa lỗ hổng file khôi phục (chỉ nhận chủ Administrators) — 140/140 test, chạy thật bản phát hành (DESIGN_LOG [25]) |
| Thiết kế UI | 🟡 **v2.2** (2026-10-25, DESIGN_LOG [20], `docs/ui/UX_Workflows_v2.2.md`): thiết kế theo tình huống sử dụng — Thiết lập lệnh (ảnh/đèn/job/giới hạn/bộ ảnh thử một chỗ), bản nháp + áp dụng giữa hai lệnh, tham số nhanh, chụp loạt, đường tắt; Camera & đèn ở cấp máy. **Đã chốt (DESIGN_LOG [21]):** sửa khi ONLINE bằng bản nháp + chạy kèm trên sản phẩm thật + áp dụng/quay lại giữa hai lệnh; bỏ kiểm mẫu chuẩn đầu ca; giữ 8 ảnh/lệnh trong RAM, còn lại đọc từ ảnh đã lưu. Nền v2.1 (WinForms, vision riêng, 3 vai trò, control VisionPro thật) giữ nguyên |
| Git / GitHub | ✅ `songloi0730/MeoVision` (**private**, nhánh `main`, từ 2026-11-02). Không đưa lên: `dist/`, `Data/`, `reference/`, `docs/backups/` (tên thật dự án tham khảo). Trước mỗi commit: không để tên thật — dùng bí danh RefVis-* |

## Bố cục dữ liệu (DESIGN_LOG [11])

**Từ Session 23 (DESIGN_LOG [26]):** `DataRoot` mặc định = `Data` cạnh exe — chương trình + `.vpp` + cấu hình + tài khoản đi
cùng nhau; ảnh/kết quả/log ra ổ khác bằng `ImagesRoot`/`ResultsRoot`/`LogsRoot` (ổ không có → ghi tạm vào `Data` + báo vàng).
Sách (Ch17/23/24) còn mô tả cách cũ dưới đây:
Chương trình (exe, DLL, `languages`, `appsettings.json`) tách hẳn khỏi `DataRoot` (`%ProgramData%\MeoVision`):
`machine.json` + `machine\calibration` (riêng từng máy, gán VAI TRÒ cho thiết bị thật) và
`products\<ProductId>\{product.json, jobs\*.vpp, golden, samples}` (tự chứa, chỉ tham chiếu vai trò).
`machine.json`: xong ở Ch18 (mẫu `src/MeoVision.UI/machine.example.json`); `product.json`: Ch19.

## Ràng buộc với sách

- **Chương 24** (mẫu `tools/code_templates/Chapter_24.tpl.md`) trích `Hosting/ReleaseManifest.cs`, `Hosting/InstallChecks.cs`,
  `tools/publish.ps1`, `Security/UserStore.cs` (ApplyRecovery, AdministratorsOwner), `Maintenance/StationBackup.cs`,
  `Commands/ReplayRunner.cs`, `Setup/SetupForm.cs` (RunFolder, ExpectedLabel, ReplayStatus), `Maintenance/DiagnosticBundle.cs`,
  `UI/Program.cs` (RunCheck, LoadUsers). Chương 23 tiêm lại được (cùng neo); **Ch22 là BẢN CHỤP**.
- Test hay hụt khi máy bận: `Commands_Of_Two_Cameras_Run_In_Parallel` (ngưỡng 450 ms đồng hồ thật).

- **Chương 22** (mẫu `tools/code_templates/Chapter_22.tpl.md`) trích `Core/Commands/DisplayFrame.cs`,
  `CogVisionJob.cs` (Run có request), `CommandEngine.cs` (RunAsync phần job + Finish), `ResultFrameView.cs` (Post, Flush),
  `RecordView.cs`, `MainForm.cs` (_postTargets, OnCommandCompleted, ONLINE giữ 1 s), `RecentFrames.cs`,
  `samples/products/P-1024/product.json` (T3 + tunables), `ProductLoader.cs` (CheckTunables), `ProductStore.cs` (OpenDraft,
  Apply, SwitchOrRestore, RollBack, JsonSettings, SetCurrent), `DraftSession.cs` (IsChanged, Run), `ShadowRunner.cs`
  (OnCompleted, Compare), `SetupForm.cs` (RunDraft), `JobEditorForm.cs` (Edit), `UI/Program.cs` (đăng ký Ch22).
  **Ch20 nay là BẢN CHỤP** — không tiêm lại Ch20 (IVisionJob, CogVisionJob.Run, CommandEngine.RunAsync, CommandResult
  đã đổi); Ch21 vẫn tiêm lại được (các đoạn Ch21 trích từ Program.cs không đổi).

- Chương 21 trích (mẫu `tools/code_templates/Chapter_21.tpl.md`): `Comm/MessageProtocol.cs`, `Comm/CommandService.cs`,
  `Comm/TcpChannel.cs`, `Comm/Plc/McProtocolClient.cs`, `Comm/Plc/PlcChannel.cs`, `UI/machine.plc.example.json`,
  `Calibration/CalibrationService.cs`, `Vision/Commands/CogNPointCalibrationBuilder.cs`, `UI/Program.cs`.
- Chương 20 trích (mẫu `tools/code_templates/Chapter_20.tpl.md`): `Core/Commands/IVisionJob.cs`, `CommandResult.cs`,
  `ProductLoader.cs` (cuối Load), `LoadedProduct.cs`, `ProductStore.cs` (Acquire, SetCurrent), `CogVisionJob.cs`,
  `CogCalibrationLoader.cs`, `samples/products/P-1024/product.json` (lệnh T2), `CommandEngine.cs` (ExecuteAsync,
  RunAsync), `CognexGigECamera.cs` (khung hụt), `DeviceManager.cs` (catch Transient), `ResultJudge.cs`, `Program.cs`.
  **Ch19 nay là BẢN CHỤP** — không tiêm lại Ch19 (ProductLoader, ProductStore, CogJobInspector, product.json đã đổi).
- Chương 19 trích: `machine.example.json` (channels), `MachineConfig.cs` (ChannelConfig), `MachineConfigLoader.cs`
  (CheckChannels), `samples/products/P-1024/product.json`, `ProductConfig.cs`, `ProductLoader.cs`,
  `CogJobInspector.cs`, `ScriptDialogGuard.cs`, `ProductBackup.cs`, `ProductStore.cs`, `Program.cs`,
  `ProductStartup.cs` (mẫu `tools/code_templates/Chapter_19.tpl.md`). Ch18 nay là BẢN CHỤP — không tiêm lại
  Ch18 (đã sửa MachineConfig.cs, machine.example.json, IDevice.cs, DeviceStartup.cs, DeviceManager.cs, Program.cs).
- Chương 18 trích thêm: `machine.example.json`, `MachineConfig.cs`, `IDeviceFactory.cs`, `MachineConfigLoader.cs`,
  `IDevice.cs`, `CameraImage.cs`, `CogCameraImage.cs`, `CognexGigECamera.cs`, `FolderCamera.cs`, `DeviceManager.cs`,
  `VisionProLock.cs`, `LineChannels.cs`, `AsciiLightController.cs`, `DeviceTests.cs`, `RawImageConverter.cs`,
  `Program.cs`, `DeviceStartup.cs` (mẫu `tools/code_templates/Chapter_18.tpl.md`). Ch17 là BẢN CHỤP —
  không tiêm lại Ch17 sau khi Ch18 đã sửa `Program.cs`/`CognexVisionRuntime.cs`.
- File đã được trích vào Chương 17: `Directory.Build.props`, `MeoVision.Vision.csproj`, `AppPaths.cs`,
  `appsettings.json`, `DataFolderCheck.cs`, `StartupSequence.cs`, `IVisionRuntime.cs`,
  `CognexVisionRuntime.cs`, `VisionRuntimeCheck.cs`, `Program.cs`, `SingleInstanceGuard.cs`,
  `RunMarker.cs`, `CrashReporter.cs`, `Msg.cs`, `vi-VN.json`, `JsonLocalizer.cs`, `UiText.cs`,
  `MainForm.cs`, `LanguageFileCheck.cs`, `LocalizationTests.cs`, `ShutdownSequence.cs`.
- Sửa các file đó → chạy ở thư mục sách:
  `python tools/inject_code.py tools/code_templates/Chapter_17.tpl.md output/Chapter_17_KhungDuAnVongDoi.md`.
  Script dừng với "KHONG THAY NEO" nếu một neo trích dẫn không còn → sửa neo trong mẫu.
- Dòng code ≤ ~95 ký tự (khổ sách); tránh dòng trống bên trong khối `///`.

## TODO

**Phân tích khối chức năng + đề xuất D1–D10: `docs/PHAN_TICH_KHOI_CHUC_NANG.md`** (D1–D5 xong Session 25).
**Việc làm dần từ dự án tham khảo: `docs/BACKLOG_TU_DU_AN_THAM_KHAO.md`** (P1 trước: C1, S1, S2, T1, U1, B1).
Cài đặt chạy thật của máy này: `D:\MeoVision\2.0.261031\` (chương trình + Data), ảnh/kết quả/log `D:\MeoVisionData\`;
bản Debug ghi ảnh/log vào `D:\MeoVisionDev\` nhưng Data vẫn trong OneDrive (sẽ báo vàng — chỉ dùng để phát triển).

0. **Session 23 còn dở:** (a) ✅ exe Debug chạy lại, tài khoản nạp trước VisionPro; (b) ✅ sách Ch17/Ch24 đã sửa, xuất
   bản; (c) chụp lại Hình 22.2, 22.3, 23.1, 23.3, 23.4 (giao diện mới, nền xám); (d) ✅ phát hành `2.0.261030` + nâng cấp
   thật từ `2.0.261029`; (e) nhiều camera trong một lệnh mới kiểm bằng test, chưa có
   job mẫu hai ảnh chạy thật; (f) Hiệu chuẩn, Giao tiếp, Cài đặt: form chưa xây.

1. Ch20 — bộ máy chạy lệnh: giữ job đã nạp ở bước "Sản phẩm" (không nạp hai lần), mọi lần nạp `.vpp` qua
   `ScriptDialogGuard`; đèn → chụp → chạy ToolBlock → đọc output → bù/giới hạn → kết quả có kiểu; mỗi camera
   một hàng đợi. Ch21: kênh (TCP server, PLC Mitsubishi MC 3E trước, vùng D/M theo kênh), lệnh `SW` đổi sản
   phẩm qua `ProductStore.Switch`, lệnh sai kênh → `ERR,CHANNEL`.
2. ~~Repo GitHub~~ — xong 2026-11-02: `songloi0730/MeoVision` (private).
3. Ch20–24 theo bảng trong DESIGN_LOG [9].
