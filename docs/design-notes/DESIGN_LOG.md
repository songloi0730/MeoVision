# MeoVision — Nhật ký quyết định kiến trúc (Design Log)

> **Quy ước (từ 2026-07-25, theo yêu cầu tác giả):** toàn bộ quyết định kiến trúc của MeoVision ghi
> trong **một file duy nhất**, theo thứ tự thời gian — KHÔNG tạo file `NNNN-tieu-de.md` riêng nữa.
> Khi có quyết định/phát hiện mới: thêm một mục `## [N] YYYY-MM-DD — Tiêu đề` mới vào CUỐI file này,
> không sửa nội dung các mục cũ (đúng tinh thần ADR: quyết định cũ không bị xoá lịch sử, chỉ bị
> bổ sung/đảo ngược bởi mục mới hơn, có ghi rõ "đảo ngược mục [N]" khi áp dụng).
>
> File này **hợp nhất 5 file trước đây** (`0001-meovision-architecture-decisions.md` đến
> `0005-camera-lighting-communication-and-net48-runtime-proof.md`, đã xoá sau khi hợp nhất) — nội
> dung giữ nguyên, chỉ đổi từ 5 file thành 5 mục trong 1 file. Mọi chỗ trong dự án từng ghi
> "xem 0004"/"0002" nay hiểu là "xem mục [4]"/"[2]" trong file này.

---

## [1] 2026-07-22 — Mục tiêu, kiến trúc ban đầu, và các phương án đã cân nhắc

MeoVision là chương trình xử lý ảnh công nghiệp dùng làm **ví dụ chi tiết, chạy được thật**, cho
sách "Xử lý ảnh công nghiệp với Cognex VisionPro"
(`C:\Users\dinhn\OneDrive\Study\Chung\VisionPro_Image_Processing_Book`). Khác với sách trước
("C# cho Automation Machine"), nơi "MeoFrame" chỉ là tên minh hoạ dùng lại qua nhiều chương chứ
không phải một solution chạy được — MeoVision **phải build và chạy thật** (ít nhất ở chế độ giả
lập không cần phần cứng), vì mã nguồn được trích dẫn trực tiếp vào sách và tác giả cần chạy demo
khi trình bày.

### Bối cảnh và nguồn tham khảo đã dùng để quyết định

1. **Sách VisionPro (chính nó)** — 16 chương đã viết xong đã ngầm định một kiến trúc: Chương 14
   dạy `MeoVision.UI → MeoVision.Application → IVisionEngine → MeoVision.Engine` (interface ranh
   giới, chỉ `MeoVision.Engine` được `using Cognex.VisionPro.*`) song song với
   `SimulatedVisionEngine`.
2. **AM.AutoFrame** (`C:\Users\dinhn\OneDrive\C\VS\AM.AutoFrame`) — dự án C# thật của tác giả, cùng
   người, cùng gu kiến trúc (3 tầng Master/Station/Mechanism, DI, simulation parity, ISA-88). Có 2
   phát hiện quan trọng lúc đó:
   - ADR `0008-vision-process-separation.md` của AM.AutoFrame: đã **thực nghiệm bằng 6 spike
     thật** rằng nạp `.vpp`/`CogSerializer` in-process trên .NET 9 gây `SEHException` cấp native
     (do .NET 9 đã gỡ `BinaryFormatter`) — không an toàn cho phần mềm điều khiển máy. Quyết định
     của AM.AutoFrame: chạy VisionPro trong process .NET Framework 4.8 riêng, IPC.
   - ADR `0007-vision-module-design.md` của AM.AutoFrame: đã tự phản biện kiến trúc RefVis-A (ẩn
     danh, xem `docs/private/project-reviews/Review_RefVis-A.md` trong dự án sách) — kết luận:
     vision là một capability tái dùng của máy, không phải toàn bộ máy.
3. **RefVis-A** — một ứng dụng vision WinForms thật, kiến trúc single-project + singleton toàn cục
   + kiểu Cognex rò rỉ khắp nơi + state machine kiểu mảng bước tay + Thread/Stopwatch busy-wait.
   Dùng làm ví dụ "trước" (anti-pattern) đối lập trực tiếp với kiến trúc MeoVision — không sao
   chép bất kỳ dòng code nào, chỉ tổng quát hoá bài học.
4. **Nghiên cứu GitHub (Session 1)**: kết luận ban đầu "không có repo public đáng kể nào tích hợp
   Cognex VisionPro" — **kết luận này SAI, đã đính chính ở mục [3] bên dưới**.

### Quyết định 1 — Vị trí đặt project

Đặt tại `C:\Users\dinhn\OneDrive\C\VS\MeoVision` — cùng cấp AM.AutoFrame. Suy luận hợp lý từ ngữ
cảnh, nhưng lúc đó chưa được tác giả xác nhận tường minh (đến các phiên sau, tác giả tiếp tục yêu
cầu viết code tại đây nhiều lần — coi như xác nhận ngầm).

### Quyết định 2 — Kiến trúc tách process (ban đầu chọn, ĐÃ BỊ ĐẢO NGƯỢC ở mục [2])

**Phương án A — In-process, 1 project .NET hiện đại duy nhất:** đơn giản nhất nhưng ADR 0008 của
AM.AutoFrame đã chứng minh bằng spike thật rằng `.vpp`/`CogSerializer` gây crash native trên .NET
9 — không dùng được.

**Phương án B — Tách process: `MeoVision.Engine` (.NET Framework 4.8) ↔ `MeoVision.Application/UI`
(.NET 9) qua IPC JSON. ✅ CHỌN lúc đó (sai — xem mục [2]).** Lý do lúc đó: khớp bằng chứng thực
nghiệm từ AM.AutoFrame, khớp Chương 14 sách, `SimulatedVisionEngine` (.NET 9) demo không cần
Cognex. Nhược điểm: nhiều project hơn, cần hiểu ranh giới IPC.

**Phương án C — Chỉ viết `SimulatedVisionEngine`, không bao giờ đụng Cognex thật:** đơn giản nhưng
mất giá trị minh hoạ SDK thật cho một cuốn sách VỀ Cognex VisionPro.

Ranh giới dữ liệu giữa hai phía (lúc chọn B): `VisionResult` trung lập — `Pass, Score, X, Y,
AngleDeg, Checks[], JobName, Timestamp, CorrelationId`.

### Quyết định 3 — Cấu trúc solution ban đầu (ĐÃ THAY THẾ ở mục [2])

7 project: `MeoVision.Core.Abstractions` (net9.0), `MeoVision.Engine` (net48 + IPC server),
`MeoVision.Engine.Simulated` (net9.0), `MeoVision.Ipc` (netstandard2.0, hợp đồng message JSON),
`MeoVision.Application` (net9.0), `MeoVision.UI` (net9.0 WPF), `MeoVision.WorkStation.Demo`
(net9.0) + 2 test project.

### Quyết định 4 — Skill/agent cần thiết

Nguyên tắc: description là routing rule, mỗi skill một việc, đẩy chi tiết dài xuống file phụ, có
ví dụ cụ thể. Bộ skill ban đầu: `meovision-engine-patterns`, `meovision-ipc-contract` (sau đó xoá
ở mục [2]), `meovision-sim-parity`, `meovision-book-sync-check`, `meovision-ref-insight-extractor`
+ agent `meovision-code-reviewer`.

### Quyết định 5 — Tài liệu tham khảo bổ sung

`reference/Outline_To_Book_Mapping.md`, `reference/Code_Style_Guide.md`.

### Lộ trình ban đầu (V0-V4, đã đơn giản hoá ở mục [2])

V0 (Core.Abstractions+Engine.Simulated+Application, console) → V1 (UI WPF) → V2 (Ipc+Engine rỗng)
→ V3 (Engine thật) → V4 (WorkStation.Demo). Mỗi bước cần trao đổi lại với tác giả trước khi sang
bước sau.

---

## [2] 2026-07-22 — Đảo ngược Quyết định 2 của [1]: bỏ tách process/IPC, net48 WinForms nguyên khối

> Tác giả phản biện trực tiếp: kiến trúc tách process (.NET 9 ↔ .NET Framework 4.8 qua IPC) ở mục
> [1] Quyết định 2 **rủi ro không chạy được** — đúng, và lý do gốc rễ là đã áp sai một quyết định
> của dự án KHÁC (AM.AutoFrame) vào MeoVision mà không kiểm tra lại tiền đề có còn đúng không.

### Sai lầm ở mục [1] là gì

Mục [1] copy nguyên lý do IPC-split từ AM.AutoFrame ADR 0008 mà không xét: **AM.AutoFrame cần
tách process vì bản thân nó đã cam kết .NET 9/WPF/Prism cho toàn bộ ứng dụng** (nhiều module
hardware khác, HMI framework, DryIoc...) — vision chỉ là MỘT capability gắn vào một app .NET 9 đã
tồn tại từ trước. **MeoVision không có ràng buộc đó** — nó không phải gắn vào một app .NET 9 nào
cả, mục đích duy nhất là minh hoạ VisionPro.

Bỏ tiền đề sai đó đi, tiền đề còn lại là: VisionPro chính thức hỗ trợ .NET Framework, không hỗ trợ
.NET Core cho bản 9.x — và RefVis-A đã chứng minh một app WinForms/net48 nguyên khối chạy VisionPro
production thật, ổn định, nhiều năm.

### Quyết định lại

**Đảo ngược Quyết định 2 của mục [1].** Toàn bộ MeoVision chạy **.NET Framework 4.8, một process
duy nhất**. Bỏ hẳn `MeoVision.Ipc`, bỏ ranh giới process. Giữ nguyên tinh thần phân lớp (interface
`IVisionEngine`, `SimulatedVisionEngine` song song) nhưng đây giờ là **ranh giới trong-process**
(nhiều project/assembly, cùng AppDomain/process net48).

| | A — 1 process net48 (WinForms) ✅ CHỌN LẠI | B — tách process (mục [1] cũ) |
|---|---|---|
| Rủi ro "không chạy được" | Thấp — Cognex hỗ trợ chính thức, RefVis-A chứng minh thật | Cao hơn hẳn — thêm IPC transport, đồng bộ 2 project khác framework |
| Độ phức tạp cho người đọc | Thấp — 1 solution, F5 chạy | Cao — phải hiểu 2 process, hợp đồng IPC |
| Dùng control có sẵn của Cognex | Trực tiếp — cùng WinForms/net48 | Phải qua `WindowsFormsHost` |
| Test không cần VisionPro thật | Vẫn được — `SimulatedVisionEngine` project khác, net48 | Được, nhưng A cũng làm được |
| Khớp mục tiêu MeoVision | Đúng trọng tâm | Tối ưu nhầm mục tiêu (tưởng cần giống AM.AutoFrame) |

**A thắng ở mọi tiêu chí liên quan đến MeoVision cụ thể.**

### Kiến trúc solution mới (thay thế Quyết định 3 của mục [1])

```
MeoVision/
  MeoVision.sln                     — mọi project net48
  src/
    MeoVision.Core/                 — interfaces + POCO: IVisionEngine, VisionResult, VisionRecipe.
                                       KHÔNG project nào khác được using Cognex.VisionPro.* trừ
                                       MeoVision.Engine (ranh giới TRONG PROCESS, enforce bằng
                                       review/skill, không phải process OS)
    MeoVision.Engine/                — DUY NHẤT reference Cognex.VisionPro.* thật
    MeoVision.Engine.Simulated/       — SimulatedVisionEngine, không Cognex, net48
    MeoVision.Application/            — recipe/cycle/alarm/logging, chỉ phụ thuộc IVisionEngine
    MeoVision.UI/                     — WinForms (net48)
    MeoVision.WorkStation.Demo/       — mô phỏng chu trình PLC↔Vision
  tests/
    MeoVision.Engine.Simulated.Tests/
    MeoVision.Application.Tests/
```

**Đã xoá:** `MeoVision.Ipc`, skill `meovision-ipc-contract`. Lộ trình rút gọn còn V0→V1(UI)→V2
(Engine thật)→V3 (WorkStation.Demo).

---

## [3] 2026-07-23 — Kiểm chứng bằng 5 repo GitHub thật: net48 đúng, "chỉ WinForms" thì không

> Bổ sung cho mục [2] — không đảo ngược quyết định net48, mà **thu hẹp phạm vi** của một phần
> quyết định đó (ép buộc WinForms) sau khi có bằng chứng thật.

### Bối cảnh

Sau khi chốt mục [2], tác giả gửi thêm 5 URL GitHub tìm được (`RefVis-G`,
`RefVis-L`, `RefVis-M`, `RefVis-E`, `RefVis-K`) kèm
nhận xét nghi ngờ đúng: "có vẻ tôi đã nhầm có thể viết mã không chỉ giới hạn bởi net framework
winform". Câu này gộp 2 trục độc lập: (a) .NET Framework vs .NET hiện đại (runtime), (b) WinForms
vs WPF (UI framework, không liên quan tới (a) — WPF chạy được cả trên net48 lẫn net6+).

### Phương pháp

`git clone` cả 5 repo về máy (phép tường minh của tác giả), đọc trực tiếp `.csproj` và `grep` API
`Cog*` thật trong `.cs`/`.xaml` — không suy đoán từ tên file/README.

### Kết quả (5 repo, 6 project con dùng Cognex thật)

| Repo (tác giả) | Target Framework thật | UI | Cog* dùng thật trong code? |
|---|---|---|---|
| `RefVis-M` | `net48` (SDK-style) | **WPF** + WindowsFormsHost | ✅ `CogSerializer`, `CogToolBlock`, `CogRecordDisplay`, `CogToolBlockEditV2`, `CogAcqFifoTool`/`CogAcqFifoEditV2` trong `ViewModels/Pages/*.cs` thật |
| `RefVis-L` | `v4.8.1` | WinForms | Thư mục `Cognex/` + `Euresys/` riêng |
| `RefVis-G` | `v4.8`/`v4.8.1` | WinForms | README dự án kiểm tra thật; đọc lớp bộ đếm kết quả thật |
| `RefVis-E` | `v4.6` | WinForms | ✅ 18 dòng reference Cognex.VisionPro thật |
| `RefVis-K` (`VisionControl`) | `v4.7.2` | WinForms | ✅ HintPath trỏ thẳng `ReferencedAssemblies\*.dll` |
| `RefVis-K` (`Plugin.MotionControl`) | `v4.8` | — | ✅ ~50 dòng reference gần đủ bộ Cognex.VisionPro.* |

**6/6 target .NET Framework, 0/6 dùng .NET hiện đại.** Trải nhiều tác giả/năm/quốc gia (Hàn Quốc,
Trung Quốc) — hội tụ độc lập, không phải trùng hợp chép nhau.

### Kết luận 1 — net48: ĐÚNG, càng thêm chắc chắn

Không cần kiểm chứng lại trục này nữa trừ khi Cognex phát hành bản mới công bố hỗ trợ .NET hiện đại.

### Kết luận 2 — "chỉ WinForms": SAI, đã sửa

`RefVis-M` là bằng chứng thật: WPF + `WindowsFormsHost` bọc control WinForms gốc
của Cognex — đúng pattern AM.AutoFrame đã dùng độc lập. **Ràng buộc thật sự chỉ là "net48", KHÔNG
phải "net48 + WinForms".**

### Quyết định — WinForms vẫn mặc định, không còn là ép buộc

Giữ WinForms cho `MeoVision.UI` (đơn giản hơn cho ví dụ sách, khớp guide chính thức Cognex, đa số
mẫu thật khảo sát được cũng chọn vậy) — nhưng giờ là **lựa chọn có lý do**, không phải "vì VisionPro
không hỗ trợ gì khác" (sai). Nếu muốn đổi sang WPF sau này, `RefVis-M` là tham
khảo kỹ thuật cụ thể.

### Phát hiện phụ — anti-pattern lặp lại lần 2

`RefVis-G (lớp hiển thị bộ đếm kết quả)` dùng
`PGgloble.getInstance` (singleton tĩnh toàn cục) — cùng anti-pattern đã bị flag ở RefVis-A. Hai dự
án độc lập, hai khu vực khác nhau, cùng rơi vào pattern này — củng cố lý do MeoVision tránh hẳn.

---

## [4] 2026-07-24 — Sách cập nhật theo code, không phải ngược lại; V0–V3 viết code thật

> Tác giả: "viết code cho dự án MeoVision, ưu tiên chương trình theo hướng giống phần mềm thực tế
> nhất có thể chứ không chỉ là mẫu cho học tập, phần nội dung code có thể khác với sách, khi đó có
> thể điều chỉnh nội dung sách cho phù hợp và không bắt buộc chương trình và mã ví dụ trong sách
> giống nhau hoàn toàn."

### Nguyên tắc đảo ngược

CLAUDE.md trước đây ghi: *"Code ở đây phải luôn khớp code đã in trong sách."* **Đảo ngược tại
đây:** MeoVision là phần mềm thật trước, sách cập nhật THEO code sau (qua skill
`meovision-book-sync-check`, vẫn không tự ý sửa sách — báo lại tác giả quyết định). Không đổi: các
bất biến khác (net48, ranh giới `IVisionEngine` trong `Core`, sim-parity, không thêm tính năng
ngoài lộ trình) vẫn nguyên.

### Đã viết + kiểm chứng thật (không chỉ lên kế hoạch)

Toàn bộ V0→V3 build bằng MSBuild thật, test bằng `vstest.console.exe` thật, **chạy thật**:

| Giai đoạn | Trạng thái | Bằng chứng thật |
|---|---|---|
| V0 — Core+Engine.Simulated+Application+WorkStation.Demo skeleton | ✅ | Build 0 lỗi 0 warning; 15/15 test pass; chạy `.exe` → 1 chu trình, ảnh NG thật vẽ+lưu đĩa |
| V1 — MeoVision.UI (WinForms) | ✅ | Build sạch; process khởi động đúng (DI+InitializeComponent không lỗi) |
| V2 — MeoVision.Engine (Cognex thật) | ✅ (compile-verify) | Build thành công chống lại DLL Cognex 9.0 CR2 thật; mọi API `Cog*` xác nhận bằng reflection trước khi viết |
| V3 — WorkStation.Demo mô phỏng bắt tay PLC | ✅ | Chạy `.exe` → 8 chu trình, đúng Trigger/Busy/Done/Ack, 5 OK/3 NG, watchdog 2 tầng đúng công thức Ch15 §15.5.2 |

**Giới hạn trung thực:** V2 mới compile-verify — chạy thật cần tác giả tự dựng `.vpp` bằng
QuickBuild khớp `TerminalNames` (thao tác GUI, không tự động hoá được).

### Điều chỉnh ranh giới "chỉ Engine dùng Cognex"

Quá tuyệt đối, mâu thuẫn với Ch5 §5.5/Ch14 Bảng 14.2 (cho phép UI nhúng `CogRecordDisplay`). **Làm
rõ lại:** chỉ Engine đụng kiểu **xử lý ảnh** (`CogToolBlock`, `ICogImage`, `CogSerializer`); UI về
sau có thể (tuỳ chọn) nhúng control **hiển thị**, miễn không chạy logic xử lý. V1 hiện tại vẫn cố
tình giữ UI hoàn toàn không đụng Cognex.

### 5 điểm code khác sách (cần cập nhật sách sau — đã áp dụng ở Session sau, xem CHANGELOG sách
Session 18)

| # | Sách đang ghi (lúc đó) | Code thật viết | Lý do |
|---|---|---|---|
| 1 | `InspectionResult`/`InspectionRecipe` | `VisionResult`/`VisionRecipe` | Khớp ADR/skill/PROJECT_STATUS đã dùng nhất quán tên `Vision*` |
| 2 | `IVisionEngine` trong `namespace MeoVision.Engine` | Trong `MeoVision.Core.Contracts` | Đặt ở Engine buộc Application/Engine.Simulated reference project chứa Cognex chỉ để lấy interface |
| 3 | `InspectAsync(CancellationToken ct = default)` | `InspectAsync(VisionJobRequest request, ct)` | Cần chỗ mang `CorrelationId` + cấp ảnh test/replay |
| 4 | `byte[] RawImageBytes` | `System.Drawing.Bitmap? Image` | Bỏ vòng encode/decode vô ích; `CogImage8Grey` có ctor/ToBitmap nhận thẳng Bitmap |
| 5 | §14.6.2: giả lập vẫn chạy `CogToolBlock` thật | Giả lập THUẬT TOÁN hoàn toàn | Mọi tài liệu khác đều nói "không cần license để chạy demo" |

---

## [5] 2026-07-24 — Kiểm chứng net48 chạy THẬT + thêm ICameraSource/IPlcCommunication

> Tác giả: "hình như các bản net cao hơn có thể tương thích ngược với bản thấp hơn, hãy thử kiểm
> tra xem thực tế sử dụng như thế nào... nếu có thể hãy để 4.7.2 hoặc 4.8 thì hợp lý hơn, ngoài ra
> các phần như kết nối camera, đèn, truyền thông, giao diện thiết kế hợp lý, ngoài ra việc mô
> phỏng có thể không cần yêu cầu quá cao vì phần vision có thể thử nghiệm với camera công nghiệp
> thật."

### Phần 1 — net48 chạy runtime thật, không chỉ lý luận

Callout sách Chương 5 nêu `Cognex.VisionPro.dll` biên dịch cho .NET Framework 4.0 và LÝ LUẬN rằng
.NET Framework nâng cấp tại chỗ nên chạy tốt trên 4.8 — chưa phải bằng chứng runtime. Đã làm:
1. Xác nhận .NET Framework THẬT cài máy qua registry (`NDP\v4\Full`, `Release=533509`) = **4.8.1**.
2. Dựng THẬT `CogImage8Grey` từ `Bitmap` thật, `ToBitmap()` roundtrip, `Dispose()` — thành công.
3. Dựng THẬT `CogToolBlock` rỗng, đọc `Inputs`/`Outputs` — thành công.

**Bài học quy trình:** lần thử đầu báo lỗi "Constructor not found"/"PSObject cannot be converted"
— KHÔNG phải lỗi tương thích .NET, mà PowerShell tự bọc object trong `PSObject` khi Invoke qua
mảng; phải unwrap (`.PSObject.BaseObject`) trước. Suýt kết luận nhầm "không tương thích" từ lỗi
công cụ kiểm tra.

**Kết luận:** MeoVision chưa từng target net40 — mọi `.csproj` luôn `net48` từ mục [2]/[3]. "v4.0"
chỉ là thông tin về bản thân DLL Cognex.

### Phần 2 — Camera, đèn (strobe), truyền thông

**Sách dạy gì (Ch6, Ch15):** thu ảnh thật = `CogAcqFifoTool` bọc `ICogAcqFifo`, tạo qua
`ICogFrameGrabber.CreateAcqFifo`; trigger phổ biến nhất = Semi (Bảng 6.2). "Đèn" trong ngữ cảnh
vision = **strobe đồng bộ exposure** (§6.2.5: `StrobeEnabled`/`StrobeDelay`/`StrobePulseDuration`)
— không phải đèn tháp báo trạng thái. Truyền thông: giao thức Trigger/Busy/Done/Ack cố tình trung
lập phương tiện truyền, không có class I/O cụ thể trong sách.

**AM.AutoFrame:** `ICameraDevice.SetLightAsync(bool)` (đèn gắn camera) tách biệt hẳn với
`ILightController`/`TowerLightState` (đèn tháp andon) — xác nhận đây là 2 khái niệm khác nhau.

### Quyết định thiết kế

1. `ICameraSource` (Core, mới) — tách khỏi `IVisionEngine` đúng ranh giới "chụp"≠"xử lý".
   `CognexVisionEngine` nhận `ICameraSource?` tuỳ chọn.
2. Đèn = tham số trên `VisionRecipe` (`TriggerMode`/`StrobeEnabled`/`StrobeDelayUs`/
   `StrobePulseDurationUs`), không phải interface riêng.
3. `IPlcCommunication` (Core, mới) — hình thức hoá 4 tín hiệu, chỉ `bool`, không biết `VisionResult`.
4. Phương tiện truyền thật CHƯA CHỌN lúc đó (xác nhận qua câu hỏi trực tiếp) — chỉ
   `SimulatedPlcCommunication` + `NotImplementedPlcCommunication` (throw rõ ràng).
5. Mô phỏng camera CỐ TÌNH đơn giản (`SimulatedCameraSource` — khung xám trơn).

### Kiểm chứng thật

`CognexCameraSource` build thành công chống lại DLL Cognex FGGigE thật (mọi API xác nhận bằng
reflection trước khi viết). Toàn bộ solution (8+2 project) build sạch. 15/15 test cũ vẫn pass.
`MeoVision.WorkStation.Demo.exe` chạy lại đúng sau refactor `IPlcCommunication`.

---

## [6] 2026-07-25 — Hợp nhất design-notes thành 1 file; chọn TCP 127.0.0.1 làm phương tiện truyền

> Tác giả: chia sẻ danh sách 10 thành phần chuẩn của một phần mềm vision công nghiệp (thu nhận ảnh,
> tiền xử lý, thư viện tool, job/recipe, decision logic, communication, HMI, data logging, runtime/
> deployment, diagnostics — xem CHANGELOG để có nguyên văn), yêu cầu tiếp tục xây dựng theo đó; yêu
> cầu gộp các file `design-notes` thành 1 file thay vì tách nhiều file (mục này chính là kết quả);
> đề xuất dùng TCP 127.0.0.1 cho phần truyền thông (giải quyết câu hỏi còn treo ở mục [5].4); yêu
> cầu xem lại RefVis-A một lần nữa để xây "dự án hoàn chỉnh".

### Quyết định hợp nhất design-notes

Xem header đầu file này — áp dụng ngay từ bây giờ, không tạo file `NNNN-*.md` nữa.

### Quyết định — Giao thức dây (wire protocol) cho `TcpPlcCommunication`

`IPlcCommunication` (mục [5]) đã hình thức hoá đúng 4 tín hiệu Trigger/Busy/Done/Ack, cố tình chỉ
`bool`, trung lập phương tiện truyền. Giờ cần chọn: (a) khuôn dạng byte truyền qua TCP, (b) ai là
TCP client/server.

**(a) Khuôn dạng bản tin — 3 phương án:**

| Phương án | Mô tả | Ưu điểm | Nhược điểm |
|---|---|---|---|
| A — Nhị phân thô (1 byte/bitmask) | Mỗi tín hiệu 1 byte, hoặc gộp bitmask | Giống thật cách PLC trao đổi coil/register (Modbus...); nhỏ gọn, nhanh | Khó đọc bằng mắt khi debug bằng console; cần tự thiết kế framing (TCP là stream, không có ranh giới message) |
| B — Text theo dòng (`TRIGGER\n`, `BUSY 1\n`...) ✅ CHỌN | ASCII, mỗi lệnh 1 dòng | Đọc được trực tiếp trong console `MeoVision.PlcTestReceiver` — đúng mục đích "để tác giả tự kiểm tra bằng mắt"; framing miễn phí nhờ ký tự xuống dòng; nhiều hệ thống scanner/vision-PLC bridge thật cũng dùng ASCII line-based | "Nặng" hơn 1 byte thô — nhưng với tần suất 1 chu trình vision (không phải servo real-time), chênh lệch không đáng kể |
| C — JSON theo dòng (length-prefix hoặc newline-delimited) | Bản tin có cấu trúc, dễ mở rộng mang thêm dữ liệu (VD Score) sau này | Linh hoạt nhất nếu sau này cần mang `VisionResult` qua kênh này | Over-engineering cho đúng 4 tín hiệu bool hiện tại — sách Ch15 cố tình giữ kênh này tách khỏi dữ liệu vision |

**Chọn B** — lý do quyết định: mục đích chính của `MeoVision.PlcTestReceiver` là để **con người** xem
trực tiếp tín hiệu chạy qua console và tự gõ lệnh gửi lại (TRIGGER/ACK) — text ASCII đọc được ngay
lập tức, không cần công cụ phụ để giải mã. Nếu sau này cần mang thêm dữ liệu, đổi sang C không phá
vỡ `IPlcCommunication` (interface không đổi, chỉ đổi implementation).

**(b) Ai là client, ai là server — 2 phương án:**

| Phương án | Mô tả | Ưu điểm | Nhược điểm |
|---|---|---|---|
| 1 — Vision = TCP client, PLC = server ✅ CHỌN | `MeoVision.WorkStation.Demo` chủ động `ConnectAsync` tới `127.0.0.1:5050`; `MeoVision.PlcTestReceiver` là `TcpListener` đứng chờ | Khớp mô hình thật phổ biến: PLC/gateway là node cố định luôn bật, các thiết bị khác (vision, HMI, scanner) kết nối tới nó; map tự nhiên "receiver = đóng vai PLC đứng chờ" đúng như tác giả mô tả ("phần mềm khác để nhận") | Nếu receiver test chưa bật, `ConnectAsync` của Vision thất bại — nhưng đây CŨNG là hành vi đúng cần dạy (Ch15: "PLC chưa sẵn sàng" là tình huống thật) |
| 2 — Vision = TCP server, PLC = client | Vision đứng chờ, PLC/test tool chủ động kết nối tới | Cũng là mô hình thật (một số dây chuyền gọi vision như 1 service) | Không khớp trực giác "receiver nhận" của tác giả — receiver sẽ phải chủ động gọi ra thay vì chỉ đứng nghe |

**Chọn 1** — khớp đúng mô tả của tác giả ("phần mềm khác để **nhận** và thử kiểm tra") và là mô hình
phổ biến hơn trong thực tế (PLC/gateway là node mạng cố định).

### Đã triển khai

- `MeoVision.Core.Contracts.IPlcCommunication` — KHÔNG đổi (đã trung lập phương tiện từ mục [5]).
- `MeoVision.WorkStation.Demo.TcpPlcCommunication` (+ `TcpPlcCommunicationOptions`: Host/Port/
  ConnectTimeoutMs) — implements `IPlcCommunication` bằng `TcpClient`/`NetworkStream`/
  `StreamReader`/`StreamWriter` ASCII line-based. Cancel giữa chừng: net48 `StreamReader.ReadLineAsync()`
  không có overload nhận `CancellationToken` — dùng `ct.Register(() => client.Close())` để buộc
  đóng socket, biến `IOException`/`ObjectDisposedException` phát sinh thành `OperationCanceledException`
  đúng ngữ nghĩa (cùng tinh thần CS03 Timeout Pattern, áp dụng cho transport chứ không phải SDK call).
  `ConnectAsync` có timeout riêng (`ConnectTimeoutMs`) vì net48 `TcpClient.ConnectAsync(host,port)`
  không nhận `CancellationToken` — dùng `Task.WhenAny` với `Task.Delay`.
- Xoá `NotImplementedPlcCommunication.cs` — đã hết lý do tồn tại (placeholder chờ chọn phương tiện,
  nay đã chọn). Không giữ lại "phòng khi cần" — vi phạm nguyên tắc "không giữ code chết".
- `MeoVision.PlcTestReceiver` (project mới, net48 console, không reference bất kỳ project MeoVision
  nào khác — cố tình độc lập hoàn toàn để là công cụ kiểm tra khách quan, không bị che lấp bởi lỗi
  chung trong `MeoVision.Core`): `TcpListener` trên `127.0.0.1:5050`, in mọi dòng nhận được kèm
  timestamp, cho gõ lệnh `t`/`trigger` (gửi TRIGGER) và `a`/`ack` (gửi ACK) từ bàn phím.
- `appsettings.json` của `WorkStation.Demo` thêm section `PlcCommunication` (Transport: Tcp|
  Simulated, Host, Port, ConnectTimeoutMs) — mặc định `Tcp`; đổi `Simulated` để chạy demo độc lập
  không cần mở `MeoVision.PlcTestReceiver`.

### Kiểm chứng thật

Build MSBuild thật: 9 project (8 cũ + `MeoVision.PlcTestReceiver` mới), 0 lỗi 0 warning. 15/15
unit test cũ vẫn pass (`vstest.console.exe`, không đổi hành vi V0-V3). Chạy THẬT đồng thời 2
process riêng biệt (`MeoVision.PlcTestReceiver.exe` + `MeoVision.WorkStation.Demo.exe`, không
chỉ 1 process console như bản `Simulated` cũ) — script PowerShell tự động hoá gửi lệnh
`t`/`a` qua `Process.StandardInput`, xác nhận: 8/8 chu trình hoàn tất, đúng thứ tự
TRIGGER→BUSY 1→BUSY 0→DONE 1→ACK→DONE 0 mỗi chu trình, kết quả tổng kết OK=5/NG=3 khớp hệt
bản `Simulated` trước đây (refactor transport không đổi hành vi nghiệp vụ) — log đầy đủ ở cả
2 phía (receiver thấy đúng "[Nhận]"/"[Gửi]", demo thấy đúng `Outcome=`/`Score=`).

**Bài học quy trình (đáng ghi vì suýt gây kết luận sai):** lần thử đầu, `MeoVision.PlcTestReceiver`
liên tục báo "Lệnh không hợp lệ" cho lệnh ĐẦU TIÊN gõ vào sau khi kết nối, dù lệnh thứ hai trở đi
luôn đúng. Debug bằng cách in mã ký tự thô cho thấy chuỗi nhận được là `"???t"` (4 ký tự) thay vì
`"t"` — 3 ký tự `?` (mã 63) đứng trước đúng là dấu hiệu 3 byte không giải mã được bằng ASCII. Đọc
byte thô (bỏ qua mọi lớp decode) xác nhận: 3 byte đầu tiên nhận được là `EF BB BF` — một BOM UTF-8
thật. Nguồn gốc: script PowerShell dùng để tự động hoá kiểm thử (không phải người gõ tay) chỉ cần
CHẠM vào `Process.StandardInput.BaseStream` (dù chỉ để lấy `.BaseStream`, chưa gọi `.Write` nào)
là đã đủ để .NET âm thầm khởi tạo `StreamWriter` bọc ngoài với `Encoding.UTF8` (có preamble) và xả
BOM đó vào pipe trước khi byte thật tới. Sửa SAI lần đầu: tắt `detectEncodingFromByteOrderMarks`
trên `StreamReader` phía nhận — sai vì BOM vẫn là byte thật nằm trong dữ liệu, tắt dò BOM không
xoá được nó, dùng `Encoding.ASCII` decode 3 byte đó vẫn ra `"???"`. Sửa ĐÚNG: giữ
`detectEncodingFromByteOrderMarks: true` nhưng đổi `Encoding.ASCII` → `Encoding.UTF8` cho
`StreamReader` đọc stdin — để reader tự nhận diện và bóc đúng 3 byte BOM hợp lệ đó, không ảnh
hưởng gì tới người gõ tay thật (không tạo BOM). **Kết luận:** lỗi này thuộc về CÔNG CỤ KIỂM THỬ
(cách PowerShell `Process.StandardInput` hoạt động), không phải lỗi thiết kế giao thức của
`TcpPlcCommunication`/`MeoVision.PlcTestReceiver` — nhưng cách phòng thủ đúng (BOM-aware reader)
lại có giá trị thật: nếu sau này có công cụ tự động hoá nào khác cũng vô tình chèn BOM tương tự,
`MeoVision.PlcTestReceiver` vẫn xử lý đúng mà không cần biết trước. Cùng loại bài học với vụ
PowerShell `PSObject` ở mục [5] — lỗi công cụ kiểm tra dễ bị hiểu nhầm thành lỗi code thật nếu
không đọc byte thô để xác minh.

---

## [7] 2026-07-25 — Recipe đa bước điều khiển bằng cấu hình; ranh giới script trong `.vpp` (bài học RefVis-A, xem lần 2)

### Bối cảnh

Tác giả xem lại **RefVis-A** lần 2 qua lăng kính "một phần mềm chung cho nhiều máy — chỉ cần
cấu hình + file `.vpp` chứa C# script" và hỏi MeoVision có nên theo hướng đó không. Đọc code
thật RefVis-A xác nhận mô hình: shell WinForms dùng chung; mỗi sản phẩm là thư mục
`Products\{tên}\ToolBlock\NN_*.vpp` (đánh số = thứ tự bước) + `CalibrationTool\*.vpp` + file
INI khai báo tên Input/Output từng ToolBlock; phần chạy tool generic (gán `Inputs[tên]` →
`Run()` → đọc `Outputs[tên]` theo config); logic đặc thù máy nằm MỘT PHẦN trong script nhúng
`.vpp`, nhưng một phần vẫn phải hardcode trong exe (2 hàm toán dẫn hướng đặc thù máy) — tức
"generic 100% chỉ bằng config" không đạt được ngay trong chính dự án đó.

Ràng buộc hiện trường tác giả nêu (chính đáng, quyết định thiết kế):
- Nhà máy điện tử: **không có git**, mạng hạn chế.
- Cần **sửa tại hiện trường không build lại, không Visual Studio** (IPC chỉ có QuickBuild).
- Lo ngại lộ code MeoVision khi mang vào nhà máy.

### Phân tích

1. **IP:** exe net48 dịch ngược gần nguyên vẹn bằng ILSpy/dnSpy — mang exe không kín hơn mang
   source bao nhiêu; ngược lại logic trong `.vpp` thì ai có QuickBuild trên IPC đều đọc/sửa
   được. Cả hai đều không kín → bảo vệ IP là bài toán riêng (obfuscation/dongle/hợp đồng),
   KHÔNG để nó quyết định kiến trúc.
2. **Không git cắt cả hai phía:** `.vpp` là binary, càng không diff được. Hậu quả nhìn thấy
   trong RefVis-A: file vpp bản-copy/bản-test nằm lẫn bản chạy thật, thư mục recipe cũ chỉ
   "xoá" bằng cách đổi tên đánh dấu — sửa-tại-chỗ-không-truy-vết. Thay thế đúng không phải git mà là **app tự backup có vết**
   (checksum + timestamp, lưu ngay trên IPC).
3. **Nhu cầu thật = sửa hiện trường không cần build.** Recipe đa bước + ngưỡng trong config
   đáp ứng ~90%: thêm/bớt bước, chỉnh tool/ROI/pattern, đổi ngưỡng, sản phẩm mới — đều không
   build. Phần còn lại (logic quyết định xuyên bước đổi tại hiện trường) hiếm khi thật sự
   cần, và chính RefVis-A cũng không nhét hết được vào `.vpp`.

### 3 phương án + đánh đổi

| Phương án | Đặc điểm | Đánh đổi |
|---|---|---|
| A — Giữ nguyên V0–V3: 1 ToolBlock, terminal cố định | Đơn giản nhất | Máy mới nhiều bước phải sửa Engine |
| **B — Chuỗi ToolBlock cấu hình theo recipe ✅ CHỌN** | Recipe khai báo danh sách bước (vpp + tên terminal + ngưỡng + hành vi fail); Engine chạy tuần tự generic; logic quyết định vẫn ở C# app | Thêm schema recipe v2 + validate terminal lúc load |
| C — Như RefVis-A: logic trong script `.vpp` | Kỹ sư vision tự chủ tối đa tại hiện trường | Logic vô hình (không diff/test/review); sau vài năm × nhiều máy không ai biết máy nào chạy luật gì; "generic" vẫn vỡ (bằng chứng trong chính RefVis-A) |

### Quyết định — B "hiện trường hoá" + 4 điều chỉnh

1. **`.vpp` = vision, exe = quyết định** — nhưng đẩy tối đa tham số quyết định (ngưỡng, số
   lần retry, hành vi khi fail từng bước) ra recipe/config, để "sửa quyết định" tại hiện
   trường phần lớn = sửa config.
2. **Script trong `.vpp` = van xả có kỷ luật, KHÔNG cấm tuyệt đối:** chỉ dùng cho việc thuần
   vision (ghép fixture, toạ độ trung gian); kết quả chỉ ra ngoài qua terminal; app coi
   `.vpp` là hộp đen — không bao giờ phụ thuộc chuyện trong đó có script hay không.
3. **Backup không cần mạng:** app theo dõi checksum `.vpp` + config; phát hiện đổi → tự lưu
   bản cũ kèm timestamp vào `backup/`, giữ N bản (config), log rõ. Đây là "version control
   cho nhà máy không git" — gộp vào V5.
4. **`TerminalNames`: từ hằng số cố định → khai báo trong recipe theo từng bước**, giá trị
   V0–V3 thành mặc định khi migrate; validate tên terminal NGAY LÚC LOAD recipe (fail sớm,
   không fail giữa ca sản xuất).

### Hệ quả

- `docs/ROADMAP_HOAN_THIEN.md` V5 viết lại: "Recipe đa bước + versioning/backup vpp+config".
- `IVisionEngine` KHÔNG đổi; `VisionRecipe` thêm `SchemaVersion` + danh sách bước (schema
  v2, migrate từ v1 = 1 bước với terminal mặc định).
- Simulation parity giữ nguyên: `SimulatedVisionEngine` phải chạy cùng schema chuỗi bước.
- MeoVision là code sách → dạy mô hình có kỷ luật (B) thay vì mô hình tiện trước mắt (C);
  chạm nội dung Ch13/14 khi triển khai → chạy `meovision-book-sync-check`.

---

## [8] 2026-07-25 — 4 màn hình UI ưu tiên cao + UserLevel tối giản (theo khoảng trống RefVis-A)

> Tác giả: "Xem các phần mềm tham khảo xem phần mềm cần bổ sung các form/giao diện nào thì bổ sung
> cho tôi." Đã giao Agent đọc lại RefVis-A, đếm chính xác 21 form/UserControl (không phải "~35+"
> ước lượng ở mục [1]), nhóm theo 18 chức năng, đối chiếu với `MeoVision.UI` hiện có (chỉ 1
> `FormMain`, 3 tab) và với các chương sách đã viết. Kết quả đầy đủ (18 nhóm, bảng ưu tiên) ở
> `VisionPro_Image_Processing_Book/docs/private/project-reviews/Review_RefVis-A.md` mục 14.

### Nguyên tắc chọn — không phải "RefVis-A có gì thì thêm nấy"

4 nhóm được xếp **Cao** đều thoả đồng thời 2 điều kiện: (a) một chương sách ĐÃ THIẾT KẾ/VIẾT CODE
cho khái niệm đó nhưng CHƯA có màn hình đi kèm — tức "khép kín" ví dụ dở dang, không phải đề xuất
mới; (b) cần thật cho 1 trạm đơn giản (1 camera, 1 sản phẩm), không phải chỉ cần khi có
multi-camera/multi-station. Các nhóm khác của RefVis-A (đăng nhập nhiều cấp, cấu hình đa camera,
IO rời, đèn đa kênh, đa ngôn ngữ, splash, dialog dùng chung...) bị loại hoặc hạ ưu tiên chính vì
KHÔNG thoả 1 trong 2 điều kiện trên — xem đầy đủ lý do từng nhóm trong Review mục 14.

### 4 màn hình chọn xây dựng phiên này

| # | Màn hình | Chương sách đã thiết kế | Ghi chú |
|---|---|---|---|
| D | Dạy mẫu (Teach) — thu gọn: chụp ảnh → chọn vùng train → gọi `TeachPatternAsync` → backup `.vpp` | Ch14 §14.4.2 | KHÔNG nhúng full ToolBlock editor như RefVis-A (đúng lý do ranh giới `IVisionEngine` tồn tại) |
| J | Xem/lọc ảnh NG đã lưu theo ngày | Ch14 §14.5 | Đọc trực tiếp `InspectionLogEntry.SavedImagePath` qua `IInspectionResultStore` đã có — KHÔNG cần API mới trên `INgImageArchiver` |
| K | Tab xu hướng score (rolling window N chu trình gần nhất) | Ch16 §16.4 | `CycleMetrics` trong sách CHƯA tồn tại trong code thật — dùng lại `IInspectionResultStore.GetRecentAsync()` đã có, không tạo store mới (đơn giản hơn, đủ dùng) |
| M | Cấu hình chính sách lưu ảnh (số ngày giữ, ngưỡng dung lượng) | Ch14 §14.5.2 | Sửa `appsettings.json` trực tiếp — áp dụng từ lần chạy sau (không làm live-reload, chưa cần) |

### Quyết định phụ — thêm `UserLevel` tối giản (không có trong đề xuất ban đầu)

Ch14 §14.4.2 viết rõ: "Màn teach... quyền hạn giới hạn (chỉ Engineer trở lên — đúng phân quyền
`UserLevel` đã thống nhất trong hệ thống)" — nhưng grep xác nhận `UserLevel` **chưa hề tồn tại**
trong code MeoVision (khác AM.AutoFrame, nơi khái niệm này đã có thật). Xây Teach mà không có gì
gác cửa thì trái với chính thiết kế Ch14 đã in. Chọn bản tối giản — **2 cấp** (Operator/Engineer,
không phải 5 cấp như AM.AutoFrame), **1 mật khẩu Engineer cố định** đọc từ `appsettings.json` (không
hardcode trong source), phiên đăng nhập chỉ tồn tại trong bộ nhớ (mất khi tắt app — đúng thực tế
ca làm việc), KHÔNG xây `IUserService`/database user đầy đủ (thừa cho mục tiêu code sách).

### Không làm trong phiên này (ghi rõ để không hiểu nhầm là quên)

- Nhóm A đầy đủ (RefVis-A: 2 vai trò với 1 vai trò đổi mật khẩu theo ngày) — chỉ lấy phần tối
  giản đủ gác cửa Teach.
- Nhóm E (CRUD recipe đầy đủ nhiều bước), F (calibration UI), H (form IP/port PLC) — xếp
  **Trung bình**, để dành đợt sau, không phải quên.
- Global exception handler / status chip kết nối (bài học watchdog ở mục [7]/§13.3 review) —
  thuộc phạm vi V7 roadmap, không phải UI form, không trộn vào đây.

### Đã triển khai

- `MeoVision.Application.Auth`: `UserLevel` (2 giá trị), `ICurrentUserService`/`CurrentUserService`
  (phiên trong bộ nhớ, mật khẩu Engineer đọc từ `MeoVisionHostOptions.EngineerPassword`, sự kiện
  `LevelChanged`). `InspectionLogEntry` thêm `ProcessingTimeMs` (lấy thẳng từ `VisionResult`, đã có
  sẵn — không cần entity mới, dùng lại `IInspectionResultStore.GetRecentAsync()` cho cả tab Xu
  hướng lẫn form Ảnh NG, đúng nguyên tắc không thêm abstraction chưa cần).
- `MeoVision.UI`: `FormLogin` (dialog mật khẩu), `FormTeach` (chọn vùng train bằng kéo chuột trên
  `PictureBox`, tự quy đổi toạ độ control↔ảnh vì `SizeMode=Zoom` có viền đệm, gọi
  `IVisionEngine.TeachPatternAsync`), `FormNgImages` (lọc theo ngày + xem trước, đọc ảnh qua
  `FileStream` để không khoá file), `FormImageRetentionSettings` (đọc/ghi trực tiếp
  `appsettings.json` qua `JObject`, giữ nguyên mọi section khác). `FormMain` thêm tab "Xu hướng"
  (biểu đồ tự vẽ GDI+ trên `Panel.Paint`, không thêm thư viện chart ngoài) + nhóm "Công cụ kỹ sư"
  ở tab Cấu hình (nút Đăng nhập/Đăng xuất, Dạy mẫu, Ảnh NG, Cấu hình lưu ảnh — 2 nút sau chỉ bật
  khi `UserLevel.Engineer`).
- Lỗi build gặp phải: `Math.Clamp` không tồn tại trên net48 (chỉ .NET Core 2.0+/.NET Standard
  2.1+) — thay bằng `Math.Min(Math.Max(x, min), max)` ở 3 chỗ (cùng loại gotcha đã gặp với
  `ArgumentNullException.ThrowIfNull` ở mục [1]/`Guard.cs`).

### Kiểm chứng thật

Build MSBuild 9 project, 0 lỗi 0 warning. 21/21 unit test pass (`vstest.console.exe`) — 15 test cũ
+ 6 test mới cho `CurrentUserService` (đăng nhập đúng/sai mật khẩu, đăng xuất, sự kiện
`LevelChanged` bắn đúng/không bắn khi sai mật khẩu). Chạy thật `MeoVision.UI.exe` — process sống
> 2.5s, tiêu đề cửa sổ đúng ("MeoVision — Trạm kiểm tra"), xác nhận `InitializeComponent()` của
`FormMain` (đã thêm tab Xu hướng + nhóm Công cụ kỹ sư) và toàn bộ DI constructor mới
(`IVisionEngine`, `ICurrentUserService`) không lỗi. Bổ sung thêm: gắn tạm 1 nhánh `--smoketest`
vào `Program.cs` (đã xoá sau khi dùng) tự dựng CẢ 5 form (`FormMain` + 4 form mới) qua DI thật rồi
gọi `CreateControl()` (chạy `InitializeComponent()` + tạo handle Win32 thật, không cần hiển thị
cửa sổ) — cả 5 đều `OK`, xác nhận layout/wiring không có lỗi runtime ẩn mà build-time không bắt
được. **Giới hạn trung thực:** vẫn CHƯA xác minh trực quan (computer-use không resolve được exe
tự build, như đã ghi nhận từ Session 4) — chưa có ai thật sự nhìn thấy/click qua các form này.


---

## [9] 2026-10-14 — MeoVision thành khung vision đầy đủ theo kiến trúc lai (Phần VI của sách)

### Bối cảnh

Tác giả yêu cầu sách hướng dẫn **chi tiết** cách xây một phần mềm tích hợp VisionPro như hai hệ
thật đã đọc ở dự án sách (RefVis-A: phần mềm một máy ép lắp; RefVis-C: khung dùng chung của một nhà
tích hợp) — "chi tiết cách thực hiện, các nội dung chức năng cần thiết, không chỉ giới thiệu qua".
Tác giả chốt 3 điều qua câu hỏi (2026-10-14):

1. **Hình thức:** Phần VI mới của sách, 8 chương (Ch17–Ch24), mỗi chương một khối chức năng.
2. **Kiểm chứng:** **xây thật trong MeoVision** (build + test + chạy), rồi trích code vào sách.
3. **Kiến trúc:** **lai** — bảng lệnh cấu hình được như hai hệ tham khảo, nhưng kiểm toàn bộ cấu hình
   lúc khởi động, kết quả có kiểu, logic quyết định trong C# có test (đúng khuyến nghị Ch14 §14.7).

Điều này **mở rộng phạm vi** so với quy tắc "không thêm tính năng ngoài lộ trình V0–V3" — được tác
giả yêu cầu trực tiếp, nên mục này thay thế quy tắc đó cho các giai đoạn dưới đây.

### Ba phương án kiến trúc đã đặt ra cho tác giả

| Phương án | Đặc điểm | Đánh đổi |
|---|---|---|
| **Lai ✅ CHỌN** | Bảng lệnh (mã lệnh → camera/hiệu chuẩn/chuỗi ToolBlock/input/output) như hai hệ thật; kiểm toàn bộ bảng + script + terminal lúc khởi động; kết quả `VisionResult` có kiểu; quyết định (kiểm biên, ghép lệnh, bù toạ độ) trong C# có test | Nhiều code hơn hai phương án kia; phải giữ đồng thời tính "cấu hình được" và "kiểm sớm" |
| Bám sát khung nhà tích hợp (RefVis-C) | Hợp đồng terminal chuẩn, lệnh ghép, logic trong script `.vpp` | Lặp lại đúng các lỗi đã thấy (script có trạng thái, lỗi cấu hình lộ lúc chạy) — trái [7] |
| Giữ `IVisionEngine` thuần (V0–V3) | Engine có kiểu cho từng trạm | Không dạy được "một phần mềm nhiều máy" — chính nhu cầu tác giả hỏi |

### Quyết định — giữ nguyên các bất biến cũ, thêm khung bảng lệnh

Giữ: net48 một process ([2]); chỉ `MeoVision.Engine` chạm kiểu xử lý ảnh Cognex; simulation parity;
API `Cog*` kiểm chứng bằng reflection/spike trước khi viết; [7] (chuỗi ToolBlock theo cấu hình,
`.vpp` = vision, exe = quyết định, tự backup không cần git). Thêm, theo từng chương:

| Chương sách | Khối chức năng xây thật trong MeoVision | Thay/khớp lộ trình cũ |
|---|---|---|
| Ch17 Khung dự án & vòng đời | `AppPaths` (bố cục thư mục dữ liệu), `StartupSequence` (khởi động có thứ tự, đo thời gian, phân biệt lỗi chặn/cảnh báo), kiểm môi trường VisionPro (phiên bản, tính năng license), bắt lỗi toàn cục, một bản chạy (mutex), log file xoay vòng, tắt máy có thứ tự (ngắt frame grabber `Disconnect(false)` cuối cùng) | mới |
| Ch18 Lớp thiết bị | Quản lý nhiều camera theo cấu hình, chuyển buffer SDK hãng → `CogImage8Grey`, cache exposure, timeout, watchdog + reconnect; `ILightController` (RS232/TCP), strobe | V7 (giám sát) |
| Ch19 Project/recipe & bảng lệnh | `project.json` có `SchemaVersion` (camera, đèn, hiệu chuẩn, vị trí chuẩn, bảng lệnh), validate toàn bộ lúc khởi động, backup checksum | V5 ([7]) |
| Ch20 Bộ máy chạy lệnh | Pipeline đèn → chụp → gắn hiệu chuẩn → chạy chuỗi ToolBlock → đọc output có kiểu → kiểm biên → trả lời; lệnh ghép không trạng thái; retry/timeout; đo thời gian từng bước | mới (thay `InspectionCycleService` đơn trạm) |
| Ch21 Giao tiếp | TCP server bản tin chuỗi (khớp nguyên token, mã sản phẩm, số trường cố định, số thứ tự), giao thức hiệu chuẩn do máy dẫn, ghi CSV/MES | V4 mở rộng |
| Ch22 Giao diện | Màn vận hành đa camera, cấu hình bảng lệnh, hiệu chuẩn, teach/golden, xem ảnh NG | V1 mở rộng |
| Ch23 Người dùng & dữ liệu | Vai trò (thay 2 cấp), băm mật khẩu PBKDF2, tự đăng xuất khi rảnh, audit thay đổi tham số, lưu ảnh/CSV xoay vòng | [8] mở rộng, V6 |
| Ch24 Triển khai & bảo trì | Checklist cài máy mới, `app.config`/redirect khi đổi bản VisionPro, sao lưu/khôi phục, replay hồi quy, gói chẩn đoán | V8 |

### Cách làm từng chương

Mỗi chương: (1) code thật trong MeoVision + unit test + build 0 lỗi; (2) chạy thật phần chạm Cognex
(spike hoặc exe) khi có thể, ghi rõ phần nào chỉ compile-verify vì thiếu phần cứng; (3) viết chương
sách trích **nguyên văn** code đã build; (4) cập nhật `reference/Outline_To_Book_Mapping.md`.
Code trích vào sách giữ ~90 ký tự/dòng, cú pháp C# 11 có polyfill net48 như phần còn lại của repo
(code chạy *trong script* `.vpp` thì phải C# 5 — Ch12 §12.4.2).

### Hệ quả

- `ROADMAP_HOAN_THIEN.md`: V5–V8 giữ nội dung nhưng thứ tự triển khai đi theo thứ tự chương Ch17→24.
- Sách thêm Phần VI; Outline_Master của sách thêm 8 chương.
- `IVisionEngine` hiện tại vẫn giữ cho đường "một trạm, một job" (Ch14); khung bảng lệnh là lớp
  phía trên, không xoá API cũ.


---

## [10] 2026-10-14 — Dựng lại MeoVision: VisionPro bắt buộc, bỏ engine giả lập, cấu trúc 5 project

### Bối cảnh

Đang làm Ch17 theo [9], tác giả nhận xét (2026-10-14): (1) bản giả lập cho máy không có VisionPro
"không quá cần thiết" — trạm thật luôn có key, máy dev phải cài VisionPro mới có thư viện, chương
trình build ra cũng mang sẵn thư viện; (2) MeoVision có nhiều điểm không hợp lý — được phép dựng lại,
tổ chức lại cấu trúc mã, không cần giữ nội dung cũ. Tác giả chốt thêm: mã cũ chuyển vào `legacy/`;
mã mới lên một repo GitHub riêng (tên/chế độ hỏi lại trước khi tạo); Ch14 §14.6.2 viết lại thành
"chạy không cần camera".

### Những điểm của V0–V3 bị coi là không hợp lý

| Điểm | Vì sao không hợp lý cho một phần mềm VisionPro thật |
|---|---|
| `SimulatedVisionEngine` sinh số đo giả + "simulation parity" bắt buộc | Tốn code/test cho một thứ trạm thật không bao giờ chạy; không kiểm được lỗi thật của job `.vpp` |
| `VisionRecipe` mang trường của MỘT sản phẩm (`PartWidthMinMm`, `ExpectedPadCount`...) trong hợp đồng lõi | Không dùng chung được cho nhiều máy — trái mục tiêu "một phần mềm nhiều máy" ([7], [9]) |
| UI tự vẽ ảnh bằng `PictureBox` + GDI, vòng chạy bằng `Timer` UI | Ứng dụng VisionPro thật hiển thị bằng `CogRecordDisplay` (overlay của tool); chu kỳ chạy do lệnh từ máy kích hoạt, không do timer |
| Giao tiếp = mô phỏng 4 tín hiệu trong console riêng | Hai hệ tham khảo đều là server nhận lệnh chuỗi; bảng lệnh ([9]) cần đúng mô hình đó |
| Recipe/ảnh/log nằm cạnh exe; 7 project cho ít chức năng | Nâng cấp phần mềm dễ xoá nhầm dữ liệu; nhiều project làm loãng ranh giới thật |

### Phương án đã cân nhắc

| Phương án | Đánh đổi |
|---|---|
| Sửa dần trên V0–V3 | Giữ được test cũ, nhưng phải gỡ dần giả lập/recipe một sản phẩm khắp nơi — tốn hơn viết lại |
| **Dựng lại, chuyển mã cũ vào `legacy/` ✅ CHỌN** | Cấu trúc sạch theo đúng khối chức năng của Phần VI; mất test cũ (phần lớn là test giả lập) |

### Cấu trúc mới (net48, x64, VisionPro 9.x bắt buộc)

| Project | Vai trò | Cognex? | Kiểm thử |
|---|---|---|---|
| `MeoVision.Core` | Mô hình cấu hình project (camera, đèn, hiệu chuẩn, vị trí chuẩn, bảng lệnh), kết quả có kiểu, bản tin lệnh, toán dẫn hướng | Không | Unit test thuần |
| `MeoVision.Vision` | Nạp/kiểm `.vpp` (script, terminal), chạy chuỗi ToolBlock, camera Cognex GigE + camera đọc thư mục ảnh, hiệu chuẩn, kiểm môi trường/license | Có | Test tích hợp chạy THẬT trên VisionPro cài máy |
| `MeoVision.Hardware` | Đèn RS232/TCP, I/O | Không | Unit test với cổng giả |
| `MeoVision.App` | Vòng đời (khởi động/tắt, thư mục dữ liệu, bắt lỗi, một bản chạy), project + backup, bộ máy chạy lệnh, TCP server, người dùng/audit, lưu ảnh/CSV | Không (chỉ qua interface của Core) | Unit test với fake |
| `MeoVision.UI` | WinForms + `CogRecordDisplay` đa camera, các màn cấu hình | Chỉ hiển thị | Chạy thật |
| `tools/MeoVision.TestClient` | Đóng vai PLC gửi lệnh qua TCP | Không | Dùng tay / kịch bản |

Quy tắc phụ thuộc: `Core` không tham chiếu gì; `Vision`, `Hardware`, `App` chỉ tham chiếu `Core`;
`UI` tham chiếu tất cả và là nơi DUY NHẤT nối các bản cài đặt cụ thể (composition root).
"Không có camera" được xử lý bằng **camera đọc thư mục ảnh** (thuật toán VisionPro vẫn chạy thật),
không giả lập thuật toán.

### Hệ quả

- Bỏ khỏi bất biến: "simulation parity" và "mọi tính năng phải có bản Simulated" ([4], [5]).
- Giữ: net48 một process ([2]); `.vpp` = vision, exe = quyết định; tự backup không cần git ([7]);
  kiểm chứng API bằng reflection/chạy thật; sách cập nhật theo code.
- Mã V0–V3 ở `legacy/` (không thuộc solution), bản zip ở `docs/backups/2026-10-14_truoc_PhanVI.zip`.
- Sách: Ch14 §14.6.2 viết lại; các đoạn Ch13–16 nhắc `SimulatedVisionEngine` cần rà lại khi tới lượt.


---

## [11] 2026-10-14 — Một phần mềm cho nhiều loại máy: tách MÁY / SẢN PHẨM; giao diện đa ngôn ngữ

### Bối cảnh

Tác giả yêu cầu (2026-10-14): (1) mã ví dụ phải dùng được cho giao diện **đa ngôn ngữ**; (2) phần
mềm thường viết **chung cho nhiều loại máy**, cấu hình riêng và file `.vpp` gom vào một thư mục kiểu
"product" như hai hệ tham khảo để tái sử dụng; tham khảo cách làm hay, tổ chức nội dung hợp lý.

Đã đọc lại hai hệ tham khảo cho đúng hai câu hỏi này:
- RefVis-A: `PublicSystem.ini` (máy: camera theo serial, đèn, cổng, chính sách ảnh, danh sách recipe)
  + `Products/<n>.<mã>/` (AppConfig.ini + `ToolBlock/*.vpp` + `CalibrationTool/*.vpp`); ngôn ngữ là một
  lớp tĩnh trong code.
- RefVis-C: `project/main.ini` (máy: camera, hiệu chuẩn) + `project/<tên>/config.ini` + `Setup/{Calib,
  GoldenPos,Inspect}/*.vpp`; ngôn ngữ là file INI `language/<culture>/*.ini` — giao diện khớp theo
  `[TênForm] tênControl=chữ` (tự áp, hay), nhưng thông báo dùng **chính câu tiếng Trung làm khoá**, lưu
  GBK (đọc sai mã → chữ lỗi), có file "- 副本" lẫn trong thư mục, lỗi chính tả trong bản dịch.

### A. Tách cấu hình MÁY và SẢN PHẨM

| Phương án | Đánh đổi |
|---|---|
| Một file cấu hình cho tất cả (máy + sản phẩm) | Chép sản phẩm sang máy khác là chép luôn IP camera/cổng COM của máy cũ → sai ngay |
| Sản phẩm tham chiếu camera vật lý (serial/ID) | Như hai hệ tham khảo; sản phẩm không chuyển được sang máy cùng loại khác |
| **Máy khai báo phần cứng + VAI TRÒ; sản phẩm chỉ tham chiếu vai trò ✅ CHỌN** | `machine.json` gán vai trò (`TopCam`, `BottomCam`, `MainLight`…) cho thiết bị thật; sản phẩm nói "chụp bằng TopCam". Chép thư mục sản phẩm sang máy khác: chỉ cần máy đích có đủ vai trò — kiểm được lúc nạp |

Bố cục thư mục dữ liệu:

```text
<DataRoot>\
  machine.json                 # MÁY NÀY: camera/đèn/cổng + vai trò, bố cục màn hình — khác nhau mỗi máy
  machine\calibration\<id>\    # hiệu chuẩn gắn với camera VẬT LÝ của máy (Ch7 §7.7)
  products\<ProductId>\        # SẢN PHẨM: tự chứa, chép nguyên thư mục sang máy khác là dùng được
      product.json             #   bảng lệnh, ngưỡng, vai trò cần dùng, schemaVersion
      jobs\*.vpp               #   job VisionPro của sản phẩm
      golden\                  #   vị trí chuẩn (Ch7 §7.7.1)
      samples\                 #   ảnh mẫu để chạy thử/replay (Ch16 §16.3.4)
  active-product.txt
  logs\ images\ results\ backup\ audit\ diagnostics\
```

Thư mục chương trình (exe, DLL, `languages\`) KHÔNG chứa dữ liệu sản phẩm → cùng một bộ cài cho mọi
loại máy; nâng cấp phần mềm không đụng dữ liệu.

### B. Đa ngôn ngữ

| Phương án | Đánh đổi |
|---|---|
| `.resx` + satellite assembly (chuẩn .NET) | Có Designer hỗ trợ; nhưng sửa một chữ phải build lại, người dịch ở nhà máy không sửa được |
| File ngôn ngữ, khoá = câu gốc (RefVis-C) | Bắt đầu nhanh; đổi câu gốc là gãy mọi bản dịch, mã hoá ký tự dễ hỏng |
| **JSON UTF-8 theo ngôn ngữ, khoá = ID ổn định ✅ CHỌN** | `languages/<culture>.json`; thêm ngôn ngữ = thêm file, sửa tại hiện trường không build; tự áp chữ vào control theo quy ước tên; **test bắt buộc**: mọi file có đủ khoá như nhau và cùng số tham số `{0}` |

Quy tắc: thông báo nghiệp vụ là `Msg(key, args)` (không phải chuỗi đã dịch) đi qua mọi tầng, chỉ
dịch ở UI; **log ghi bằng ngôn ngữ cố định** (`LogLanguage`, mặc định en-US) để kỹ sư hỗ trợ đọc được
bất kể máy đặt ngôn ngữ gì; thiếu khoá → rơi về ngôn ngữ mặc định → rơi về chính khoá trong ngoặc, và
ghi cảnh báo một lần (không ném exception làm sập màn hình).


---

## [12] 2026-10-15 — Phát hiện "lần trước kết thúc bất thường": file đánh dấu, không dùng mutex

### Bối cảnh

`SingleInstanceGuard` ban đầu báo `PreviousInstanceCrashed` khi `WaitOne` ném `AbandonedMutexException`.
Thử thật trên exe (kill tiến trình rồi mở lại): **không có exception nào**. Lý do: mutex có tên là đối
tượng nhân Windows, tồn tại khi còn ít nhất một handle; tiến trình duy nhất giữ handle chết thì mutex bị
huỷ, lần mở sau tạo mutex mới. `AbandonedMutexException` chỉ xảy ra nếu lúc đó có tiến trình KHÁC đang
giữ handle (ví dụ bản thứ hai đang chờ) — gần như không bao giờ.

### Phương án

| Phương án | Đánh đổi |
|---|---|
| Giữ `AbandonedMutexException` | Không phát hiện được trường hợp thường gặp nhất — sai mà trông như đúng |
| Đọc log lần trước, tìm dòng tắt máy | Phụ thuộc định dạng log, log xoay vòng theo ngày |
| **File đánh dấu ✅ CHỌN** | `running.marker` trong `DataRoot`, ghi lúc khởi động (thời điểm + PID), chỉ bước cuối của tắt máy có thứ tự mới xoá. Đơn giản, test được, đúng cả khi crash/kill/mất điện |

### Hệ quả

- `RunMarker` (App/Hosting) + test; `SingleInstanceGuard` chỉ còn chặn bản thứ hai (vẫn bắt
  `AbandonedMutexException` để lấy quyền, kèm chú thích vì sao không dùng nó phát hiện crash).
- Các chương sau dùng `PreviousRunEndedAbnormally` để kiểm phần cứng kỹ hơn sau lần tắt bất thường.


---

## [13] 2026-10-15 — Lớp thiết bị (Chương 18): camera theo vai trò, camera đọc thư mục, đèn, watchdog

### Bối cảnh

Hai hệ tham khảo: RefVis-C có `CameraBase` trừu tượng + ~12 lớp con theo hãng (kể cả File và
Simulation), `LightBase` + 5 loại bộ điều khiển đèn qua RS232/TCP; RefVis-A có watchdog ping camera
nhưng cờ trạng thái không ai đọc, không tự kết nối lại, kênh socket không timeout (treo vĩnh viễn).
Yêu cầu từ [9]–[11]: sản phẩm chỉ tham chiếu VAI TRÒ; `App` không biết Cognex; không giả lập thuật
toán — chỉ thay nguồn ảnh.

### A. Ảnh đi qua tầng App bằng kiểu gì

| Phương án | Đánh đổi |
|---|---|
| Camera chỉ sống trong `Vision`, App không thấy camera | App không điều phối được timeout/kết nối lại/trạng thái chung cho mọi thiết bị; logic thiết bị dồn vào project khó unit test |
| Core định nghĩa ảnh trung lập dạng mảng byte | Mỗi khung hình phải chép sang/chép về; mất ảnh màu/16-bit/calibration gắn sẵn của Cognex |
| **Core định nghĩa `CameraImage` trừu tượng (handle mờ) ✅ CHỌN** | `Vision` cài bằng `CogCameraImage` bọc `ICogImage`; App chuyển tay không mở ra; test dùng ảnh giả. Một lần ép kiểu ở phía job (Ch20) |

### B. Loại thiết bị ánh xạ sang lớp cài đặt thế nào

`machine.json` mỗi thiết bị: `role`, `type`, `settings` (từ điển chuỗi). `IDeviceFactory` theo `type`
đăng ký ở composition root, tự kiểm `settings` của mình (lỗi trả về `Msg`). Thêm hãng camera mới =
thêm một factory, không sửa loader. (Phương án bị loại: switch/case trong loader — mỗi hãng mới sửa
code lõi; reflection theo tên lớp — lỗi chính tả trong file cấu hình thành lỗi lúc chạy khó hiểu.)

### C. Giao thức đèn

| Phương án | Đánh đổi |
|---|---|
| Mỗi hãng một lớp (RefVis-C) | Chính xác cho từng hãng, nhưng sách không kiểm chứng được giao thức hãng nào trên máy này |
| **Một lớp ASCII theo mẫu lệnh cấu hình ✅ CHỌN (+ chỗ cắm lớp riêng cho giao thức nhị phân)** | Mẫu lệnh/đáp nằm trong `machine.json` (đối chiếu tài liệu bộ điều khiển); cổng nối tiếp hoặc TCP sau một `ILineChannel`; test được bằng bộ điều khiển giả qua TCP thật |

### D. Mất kết nối

Mọi lệnh thiết bị đi qua `DeviceManager` với timeout; hỏng/quá giờ → thiết bị chuyển `Faulted`,
lệnh tiếp theo bị từ chối ngay (không treo). Watchdog nền (`System.Threading.Timer`) thử mở lại thiết
bị `Faulted` với khoảng chờ tăng dần có trần; mọi đổi trạng thái phát sự kiện mang `Msg` để UI/log
thấy. KHÔNG tự chạy tiếp chu kỳ đang dở — chặn chu kỳ là việc của Ch20.

### Hệ quả (sau khi xây, 2026-10-16)

- Đúng như phương án; thêm `VisionProLock`: lần chạm VisionPro đầu tiên chạy song song trên nhiều luồng ném
  `ArgumentNullException` ("pUnk", từ `ccComFactory.CreateInstance`) 7/40 lần hoặc làm chết tiến trình (spike
  trong test, 4 lần chạy); tuần tự hoá 40/40. Khoá mở/đóng/liệt kê/license, KHÔNG khoá Acquire. Test Vision chạy
  tuần tự.
- Kiểm thật: TCP tới cổng không ai nghe mất ~2 s mới bị từ chối → `OpenTimeout` không đặt dưới vài giây.

---

## [14] 2026-10-16 — Thiết kế giao diện trước khi code (mockup v1)

### Bối cảnh

Tác giả yêu cầu thiết kế mẫu giao diện trước, "tránh sau này phải sửa lại nhiều lần", tham khảo các hệ
vision đã đọc khi viết sách (RefVis-A/B/C), tài liệu Application Wizard của VisionPro, dự án vision dùng
Cognex và dự án UX/UI trên GitHub. Kết quả: `docs/ui/UI_Spec_v1.md` (danh mục màn, quyền, quy tắc) +
`docs/ui/meovision_ui_mockup_v1.html` (8 màn tương tác). Nguồn chi tiết ở §1 của spec.

### A. Công nghệ giao diện

| Phương án | Đánh đổi |
|---|---|
| **WinForms, tự vẽ theo token ISA-101 ✅ ĐỀ XUẤT (chờ chốt)** | `CogRecordDisplay`, `CogToolBlockEditV2`, các control chỉnh tool của Cognex đều là WinForms gốc — không vướng "airspace"; code hiện có là WinForms; giao diện phẳng ISA-101 vẽ được bằng control thường + owner-draw. Nhược: không có binding/MVVM mạnh như WPF |
| WPF + `WindowsFormsHost` cho control Cognex | Cùng công nghệ với AM.AutoFrame, MVVM; nhưng control Cognex nằm trong host luôn vẽ đè lên mọi thứ WPF (popup, toast, banner không đè lên ảnh được), focus/bàn phím giữa hai thế giới hay lỗi |
| Hai tiến trình (UI WPF, vision WinForms) | Tách sạch; nhưng thêm IPC và độ trễ hiển thị ảnh — đã loại ở [2] |

Nhất quán với AM.AutoFrame đạt bằng **cùng token màu, cùng khung 4 vùng, cùng quy tắc**, không bắt buộc
cùng công nghệ.

### B. Mô hình chế độ

| Phương án | Đánh đổi |
|---|---|
| AutoRun / Setup như RefVis-B (đổi cả bố cục màn) | Rõ ràng, nhưng hai bố cục khác nhau = người vận hành phải học hai màn |
| **Online / Offline + quyền ✅** | Một bố cục; Online khoá mọi thay đổi (nút mờ + lý do); Offline mở theo quyền. Chuyển Offline nhấn giữ 1 s vì máy sẽ nhận lỗi "vision không sẵn sàng" |
| Không có chế độ | Teach/chụp thử có thể chen vào lệnh của máy giữa ca — loại |

### C. Điều hướng

Menu lưới biểu tượng (RefVis-B) cần hai lần bấm và che màn; sidebar tốn 250 px chiều ngang ảnh. Chọn
**tab ngang trong header** (AM.AutoFrame v3): một lần bấm, tab ẩn theo vai trò, nút trong màn thì mờ +
lý do.

### D. Teach

| Phương án | Đánh đổi |
|---|---|
| Nhúng ToolBlock editor đầy đủ cho mọi người (RefVis-A, một dự án GitHub) | Linh hoạt nhất; một thao tác nhầm phá cấu trúc job, không sao lưu |
| **Wizard 6 bước + chạy thử bộ ảnh có nhãn bắt buộc trước khi lưu; editor gốc chỉ cho Kỹ sư, trên bản sao ✅** | An toàn cho phần lớn việc chỉnh (đèn, ROI, mẫu, dung sai); việc sâu vẫn làm được nhưng có rào |

### E. Vai trò

Application Wizard 3 mức, RefVis-B 4, RefVis-C 5. Chọn **4** (Vận hành · Trưởng ca · Kỹ sư · Quản trị) —
trùng AM.AutoFrame để nhà máy dùng một chính sách.

### Hệ quả

- Ch22 (giao diện) dựng theo spec này; Ch19/20/21/23 cung cấp dữ liệu cho các màn (bảng lệnh, kết quả có
  mục kiểm, bản tin thô, quyền/kiểm toán).
- Kết quả của bộ máy chạy lệnh (Ch20) phải mang danh sách mục kiểm (tên, đo, min, max, đơn vị, OK/NG) —
  màn Vận hành và Lịch sử cần đúng dữ liệu đó.
- Chưa chốt: WinForms/WPF, màn riêng hay dùng chung, ai được chuyển Online/Offline.
- **Đã thay bằng [15]** (cùng ngày): bỏ gốc AM.AutoFrame, 3 vai trò, khung theo phần mềm vision chạy thật.

## [15] 2026-10-16 — Giao diện v2: theo phần mềm vision đã chạy thật, WinForms, 3 vai trò

### Bối cảnh

Tác giả góp ý về v1: AM.AutoFrame là dự án cá nhân **chưa kiểm chứng thực tế**, không nên làm chuẩn; ưu tiên
hai phần mềm vision thật trên máy (RefVis-A, RefVis-C) và các repo vision trên GitHub (không chỉ Cognex),
"nếu cần thì tải về và chạy thử". Chốt luôn: WinForms; vision là phần mềm riêng nói chuyện với PLC hoặc PC
control (một hoặc hai máy tính); chỉ 3 vai trò Vận hành / Kỹ sư / Quản trị.

Đã làm: chạy RefVis-A và RefVis-C thật (RefVis-C cần quyền admin, tác giả mở; chụp bằng cách kiểm từng điểm
của vùng cửa sổ thuộc đúng tiến trình rồi mới chụp — không dính cửa sổ khác), hiện từng form ngoài màn hình
để chụp, clone 11 repo và quét mọi form. Kết quả: `docs/ui/UI_Spec_v2.md` + `docs/ui/meovision_ui_mockup_v2.html`.

### Phát hiện quyết định thiết kế

RefVis-A và RefVis-C do **hai nhóm khác nhau** viết, cho hai loại máy khác nhau, nhưng giống nhau ở 4 điểm:
thanh công cụ biểu tượng lớn có chữ; lưới khung ảnh đặt tên theo **chức năng**; vùng phải có Log + chạy tay;
thanh trạng thái có ô màu cho từng thiết bị và số kết nối. Đó là bố cục người vận hành/kỹ sư đã quen —
giữ nguyên, chỉ thêm phần còn thiếu.

### A. Điều hướng

| Phương án | Đánh đổi |
|---|---|
| Tab ngang trong header (v1, kiểu HMI) | Gọn, nhưng người dùng phần mềm vision không quen; đổi tab là mất màn ảnh chính |
| **Thanh công cụ biểu tượng + form riêng không modal ✅** | Đúng thói quen RefVis-A/C; màn ảnh chính luôn nằm dưới, vẫn trả lời lệnh khi đang mở form; nút to dễ bấm cảm ứng. Nhược: nhiều cửa sổ chồng nhau → mỗi form chỉ một bản, đóng bằng X, mở lại đúng vị trí |
| Thay vùng giữa bằng trang (kiểu tab của repo tản nhiệt) | Không có cửa sổ chồng; nhưng mất ảnh khi đang chỉnh — kỹ sư hay cần nhìn kết quả chạy thật song song |

### B. Vai trò

Ba vai trò. Log thật của RefVis-C chỉ có chuyển Vận hành ↔ Kỹ sư dù phần mềm có 5 mức. Vận hành không cần
mật khẩu, được đặt lại bộ đếm và chuyển sang sản phẩm có sẵn (đổi mã hàng là việc hằng ngày; máy cũng đổi
được bằng lệnh `SW`). Mọi thay đổi cấu hình: Kỹ sư. Cài đặt hệ thống + người dùng: Quản trị.

### C. Giao tiếp

| Phương án | Đánh đổi |
|---|---|
| Mỗi camera một cổng TCP (RefVis-C) | Đơn giản cho phía PC control; nhưng thêm camera = thêm cổng, nhiều kết nối phải theo dõi |
| **Một cổng, lệnh mang mã `Tn` ✅ (chờ tác giả xác nhận)** | Một kết nối, một nhịp tim; thêm lệnh không đổi mạng. Cần xử lý lệnh song song theo camera ở bên trong |
| Chỉ PLC trực tiếp | Không cần PC control; nhưng mỗi hãng PLC một giao thức, ít linh hoạt cho dữ liệu dài (mã vạch) |

Hỗ trợ cả 3 kiểu bằng cấu hình: TCP server (PC control cùng máy hoặc máy khác) và PLC trực tiếp. Bổ sung
so với RefVis-C: phân biệt **NG** với **ERR**; OFFLINE trả `ERR,OFFLINE` ngay; nhịp tim hai chiều; chỉ nhận
client đã khai báo khi chạy hai máy tính.

### D. Cài đặt nằm ở đâu

RefVis-C dồn mọi thứ vào một form Cài đặt 8 tab (cả bảng lệnh, bù dữ liệu, lưu ảnh). Tách theo **ai sửa và
thuộc về đâu**: thứ thuộc SẢN PHẨM (bảng lệnh, job, giới hạn, bù) vào form "Lệnh & job" (Kỹ sư); thứ thuộc
MÁY/HỆ THỐNG (hiển thị, thiết bị, lưu ảnh, người dùng) vào Cài đặt (Quản trị); hiệu chuẩn thuộc MÁY nhưng do
Kỹ sư làm → form riêng. Giữ cột nút chung bên phải của RefVis-C vì người dùng quen tay.

### E. Những gì thêm so với RefVis-A/C (bài học trong sách)

Thẻ OK/NG + thời gian xử lý ở đầu khung; lý do NG bằng chữ (không chỉ màu); dải 16 kết quả gần nhất; khung
mất camera nói đang thử lại lần mấy và lệnh sẽ trả gì; form Lịch sử có ảnh + overlay (RefVis-A/C bắt mở thư
mục Windows); editor ToolBlock chỉ trên bản sao, lưu = sao lưu; giới hạn/bù ngoài .vpp; nút bị khoá vẫn hiện
và nói lý do.

### Hệ quả

- Ch22 (giao diện WinForms) dựng theo `UI_Spec_v2.md`. Màn chính = `MainForm` (ToolStrip + TableLayoutPanel
  khung ảnh `CogRecordDisplay` + dock phải TabControl + StatusStrip); form = `Form` có `Owner = MainForm`.
- Ch20 (bộ máy chạy lệnh): kết quả mang danh sách mục kiểm + lý do NG; phân biệt NG/ERR.
- Ch21 (giao tiếp): một cổng TCP nhiều lệnh + nhịp tim + client cho phép; lớp PLC tách riêng.
- Chờ tác giả: Vận hành có được tự chuyển sản phẩm; một cổng hay mỗi camera một cổng; hãng PLC ưu tiên.

## [16] 2026-10-16 — Giao diện v2.1: khớp control VisionPro thật (đã nhúng thử)

### Bối cảnh

Tác giả thấy v2 "về cơ bản hợp lý", yêu cầu tham khảo thêm dự án khác cho phù hợp với các tool VisionPro, "nếu
cần thì mở chương trình và chạy để kiểm tra". Nguồn mới: chính Cognex — Application Wizard (sinh ứng dụng từ
mẫu `QuickBuild2Jobs.vpw` bằng bản dòng lệnh, build sẵn, đã chạy Run Once), QuickBuild, mẫu
`samples/Programming`; cách các repo GitHub nhúng editor. Kiểm chứng bằng spike `docs/ui/spike/UiSpike.cs`:
nhúng control thật đúng kích thước mockup, chạy job mẫu, tự chụp.

### A. Màn quản lý job đầy đủ hay ToolBlock theo lệnh

| Phương án | Đánh đổi |
|---|---|
| Nhúng nguyên QuickBuild (`CogJobManagerEdit`) như Application Wizard | Đủ mọi thứ (job, image source, Posted Items, Communications Explorer); nhưng có mở/lưu file và Navigator riêng → vượt qua thư mục sản phẩm, sao lưu, nhật ký; người dùng phải biết QuickBuild |
| **ToolBlock cho mỗi lệnh + `CogToolBlockEditV2` nhúng, ẩn nút file ✅** | Khớp bảng lệnh của MeoVision và cách các repo thật làm; lưu luôn qua MeoVision. Phải tự làm phần image source/bảng lệnh (đã có trong thiết kế) |

### B. Khung ảnh của Vận hành

`CogRecordsDisplay` mặc định có combobox record tên thô (`LastRun.CogImageFileTool1.OutputImage`) và thanh
cuộn. Chọn: tắt cả hai cho Vận hành, record "kết quả"/"gốc" khai báo theo lệnh; Kỹ sư bật lại combobox. Đo
được: ảnh tỉ lệ 1.2 trong khung 2×2 + dock 470 chỉ dùng ~65 % bề ngang → dock 380 và thu gọn được.

### C. PLC: tự làm hay thư viện NDM của Cognex

| Phương án | Đánh đổi |
|---|---|
| NDM (`Cognex.VisionPro.Comm`: EtherNet/IP, PROFINET, SLMP) | Bắt tay chuẩn Cognex có sẵn (Trigger, Inspection Complete, Job Change, Offline Request, Soft Event), control cấu hình SLMP; **chỉ tạo được qua card CC24** — thăm dò thật: 0 card, đối tượng NDM chỉ sinh từ `CogCommCard.FfpAccess` |
| **Tự làm MC 3E / Modbus TCP, tên tín hiệu theo NDM ✅** | Chạy trên mọi IPC; đổi sang card sau này ít sửa phía PLC. Phải tự viết và test giao thức |

### D. Quy tắc kỹ thuật (từ spike)

Không cho control Cognex thừa hưởng font của form (chồng chữ); chữ trong editor tiếng Anh, không dịch được;
tỉ lệ 125 % vẫn dùng được; nạp job lúc khởi động 14–18 s cần màn chờ có tiến độ; `BackColor` không đổi được
nền navy của display; ẩn được 5 nút nguy hiểm của editor (Open, Save, Save As, Reset, Protect with password).

### Hệ quả

- `UI_Spec_v2.md` lên v2.1 (§8 mới); mockup cập nhật cùng URL; spike + ảnh ở `docs/ui/spike/`.
- Ch22: lớp bọc editor (`EditorHost`) làm 3 việc — font mặc định, ẩn nút theo tooltip, chặn lưu ngoài.
- Ch21: lớp PLC tách giao thức (MC/Modbus) khỏi mô hình tín hiệu (theo NDM).

## [17] 2026-10-16 — Kênh giao tiếp khi có nhiều camera (ĐÃ CHỐT phương án C — tác giả: "tiếp tục hoàn thiện")

### Bối cảnh

Tác giả chốt: Vận hành không chuyển sản phẩm; PLC Mitsubishi (MC 3E/SLMP) làm trước. Câu hỏi còn lại: máy tính
cắm 2 camera vào 2 card mạng thì giao tiếp với máy thế nào — một cổng TCP hay mỗi camera một cổng. Lưu ý: card
mạng của camera (mạng camera, VisionPro tự lo) **không liên quan** tới cổng TCP nhận lệnh (số cổng phần mềm).

### Các chương trình khác làm thế nào (đã đọc mã / cấu hình / chạy)

| Chương trình | Mô hình | Chi tiết |
|---|---|---|
| RefVis-A (mã nguồn) | **1 server** cho 4 camera | Cổng 3000 trên 127.0.0.1; mỗi lệnh chạy trên ThreadPool (song song); trả lời mang mã lệnh + mã sản phẩm; mỗi lệnh lọc được theo client |
| RefVis-C (`main.ini`, `config.ini`) | **Kênh**: tối đa 8 server khai ở cấp máy | Cổng 10001–10008; mỗi lệnh gán `ServerIndex` (T11→1, T21/T22→2…); máy thật dùng 2 server cho camera trên/dưới |
| QuickBuild của Cognex (mẫu TCPIP, đọc bằng API) | **Thiết bị khai ở cấp ứng dụng**, job dùng chung hoặc riêng | Thiết bị TCP: Server/Client, cổng 5001, ký tự kết thúc/phân cách, hàng đợi 32–64; mẫu `QBServerSample` có **2 job dùng chung 1 cổng** |
| Repo GitHub kiểm tra ô tô (PLC LS) | 1 kết nối PLC, 1 vùng bắt tay | READY/START/END/NG/ERR + keep-alive; camera chạy lần lượt |
| Repo GitHub kiểm tra lát cắt | 1 kết nối Modbus TCP tới PLC | |

Hai họ cách làm: "**một cổng, lệnh mang mã**" (RefVis-A, mẫu QuickBuild) và "**mỗi trạm/camera một kênh**"
(RefVis-C). Cả hai chạy được trong thực tế; điều quyết định là bên trong vision có xử lý song song theo camera và
câu trả lời có mang mã lệnh hay không.

### Phương án

| Phương án | Đánh đổi |
|---|---|
| A. Luôn 1 cổng | Đơn giản nhất cho mạng/cấu hình. Hai camera chạy cùng lúc → trả lời có thể đảo thứ tự; PC control phải ghép theo mã lệnh. Với PLC: **một vùng bắt tay chỉ xử lý một trigger một lúc** → 2 camera song song không được |
| B. Luôn mỗi camera 1 cổng | Song song tự nhiên; nhưng 1 trạm dùng 2 camera nối tiếp lại phải mở 2 kết nối không cần thiết; gắn chặt camera ↔ cổng |
| **C. "Kênh" khai ở cấp máy, mỗi lệnh chọn kênh ✅ đề xuất** | Gộp ưu điểm A/B (giống RefVis-C và QuickBuild): kênh = 1 cổng TCP **hoặc** 1 vùng bắt tay PLC; máy 1 trạm → 1 kênh; trạm chạy độc lập → mỗi trạm 1 kênh. Đổi bằng cấu hình, không sửa code. Thêm một cột "Kênh" trong bảng lệnh |

### Hệ quả nếu chốt C

- `machine.json`: danh sách `channels` (id, kiểu TcpServer/TcpClient/Mc3e, địa chỉ/cổng hoặc vùng D/M, nhịp tim).
- `product.json`: mỗi lệnh có `channel`. Lệnh đến sai kênh → `Tn,ERR,CHANNEL` (như bộ lọc của RefVis-A).
- Bên trong: mỗi camera một hàng đợi; các kênh độc lập; trả lời luôn mang mã lệnh.
- Giao diện: form Giao tiếp liệt kê kênh; thanh trạng thái mỗi kênh một ô; bảng lệnh thêm cột Kênh.

## [18] 2026-10-17 — Chương 19: sản phẩm, bảng lệnh, kiểm job thật, sao lưu theo mã băm

### Bối cảnh

Tác giả: "làm tiếp" sau khi chốt UI v2.1 + kênh [17]. Chương 19 dựng `product.json` (bảng lệnh có kênh),
bộ nạp kiểm toàn bộ, kiểm `.vpp` bằng VisionPro thật, chuyển sản phẩm và sao lưu. Bằng chứng mới từ hai hệ
tham khảo: một hệ ghép tên file job từ `số_tên.vpp` (đổi tên là mất file, chỉ log đỏ), thư mục job lẫn bản
sao/bản thử, sản phẩm "xoá" bằng đổi tên tay; hệ kia sao lưu bằng một `backup.ini` cạnh file gốc.

### A. Kiểm job lúc nào

| Phương án | Đánh đổi |
|---|---|
| Lúc lệnh đầu tiên chạy (hai hệ tham khảo) | Khởi động nhanh; lỗi cấu hình lộ giữa ca |
| **Lúc nạp sản phẩm: mở từng `.vpp`, đối chiếu terminal với bảng lệnh ✅** | Bắt được không phải ToolBlock, thiếu `InputImage`, output đổi tên/sai kiểu, script không biên dịch. Tốn thời gian khởi động (đo: 1,7 s cho 2 job nhỏ) — Ch20 sẽ giữ lại đối tượng đã nạp, không nạp lần hai |

### B. Hộp thoại modal "Unable to load script" (phát hiện thật)

`CogSerializer.LoadObjectFromFile` gặp ToolBlock có script không biên dịch được thì bật MessageBox modal và
chặn luồng gọi tới khi có người bấm OK; không tìm thấy cờ tắt. Lượt test đầu treo 400 s.

| Phương án | Đánh đổi |
|---|---|
| Nạp ở tiến trình con có timeout | Cô lập tuyệt đối; nhưng không lấy được đối tượng đã nạp (Ch20 cần), phức tạp |
| Kiểm script trước bằng cách tự biên dịch | Phải tái hiện đúng trình biên dịch + tham chiếu của VisionPro; dễ lệch |
| **Bộ canh quét cửa sổ của chính tiến trình mỗi 100 ms, đọc nội dung, gửi WM_CLOSE ✅** | Đơn giản (~100 dòng), đã chứng minh bằng spike + test; phụ thuộc lớp `#32770` và chữ "script" trong tiêu đề (bản VisionPro ngôn ngữ khác có thể đổi tiêu đề — kiểm lại khi gặp) |

### C. Sao lưu

| Phương án | Đánh đổi |
|---|---|
| Một bản cạnh file (hệ tham khảo) | Bị ghi đè ở lần sửa thứ hai |
| Chép cả thư mục theo ngày | Job thật hàng chục–trăm MB, mỗi lần sửa một số là chép lại hết |
| Git | Cài/bảo trì trên máy sản xuất, file nhị phân lớn |
| **Lưu theo SHA-256, khử trùng lặp ✅** | Mỗi nội dung một lần, mỗi bản sao lưu vài KB; kiểm băm trước khi khôi phục; Prune xoá blob mồ côi |

### D. Chuyển sản phẩm

Kiểm toàn bộ sản phẩm mới bằng chính bộ nạp; hợp lệ mới ghi `active-product.txt` (file tạm + `File.Replace`)
và thay `Current`. Quyền: chỉ Kỹ sư hoặc lệnh `SW` (tác giả chốt) — chặn quyền ở Ch23, `ProductStore` ghi
`requestedBy`.

### Hệ quả

- Core: `ProductConfig`/`CommandConfig`/`LightSetting`/`DisplayRecords`/`OutputConfig`, `IJobInspector`;
  `MachineConfig.Channels`; `ILightController.MaxIntensity`.
- App: `ProductLoader` (+ `MachineView`), `ProductBackup`, `ProductStore` (log đủ mọi lỗi), `ProductStartup`,
  `MachineContext`; `MachineConfigLoader` kiểm kênh; `DeviceManager.LightLimits`.
- Vision: `CogJobInspector`, `ScriptDialogGuard`. Mẫu: `samples/products/P-1024` + `MakeSampleJobs.cs`.
- Test 39 → 54 (9 App sản phẩm, 5 kiểm job thật, 1 sản phẩm mẫu). Chạy app thật: hợp lệ có cảnh báo; 4 lỗi
  không treo, log đủ 4 dòng.
- Ch18 trở thành BẢN CHỤP (đã sửa `MachineConfig.cs`, `machine.example.json`, `IDevice.cs`, `DeviceStartup.cs`,
  `DeviceManager.cs`, `AsciiLightController.cs`, `Program.cs`) — không tiêm lại Ch18.


---

## [19] 2026-10-25 — Chương 20: bộ máy chạy lệnh (job giữ sau khi kiểm, hiệu chuẩn, làn theo camera)

### Bối cảnh

Tác giả: "tiếp tục hoàn thiện sách". Chương 20 = đèn → chụp → gắn hiệu chuẩn → chạy job → kết luận có kiểu.
Trước khi viết, đo bằng spike trên VisionPro 9.0 CR2 (`docs/private/spikes/session32/` của dự án sách):

| Đo | Kết quả |
|---|---|
| `CogToolBlock` có cách huỷ/timeout? | Không (reflection: không Abort/Timeout/Stop/Cancel) |
| Job "chỉ gán output khi tìm thấy", lần sau không tìm thấy | Output giữ số lần trước **kèm Accept**; gán `Value = null` trước Run thì sau Run còn null |
| Script ném lỗi giữa chừng (ảnh null) | RunStatus Error, output vẫn giữ số cũ |
| Cùng MỘT ToolBlock chạy trên 2 luồng, 2×100 lần | **9/200 sai, 0 exception** |
| Hai ToolBlock khác nhau song song | Không lỗi; 52 ms so với 66 ms nối tiếp (job nhỏ) |
| Dispose ảnh rồi `CreateLastRunRecord()` | `ObjectDisposedException` |
| Tool N điểm `Run()` | Ảnh ra là đối tượng mới, CHUNG pixel, 0,031 ms; cây toạ độ của ảnh VÀO cũng bị thêm space |
| `AddSpace` phép biến đổi lấy một lần lúc nạp | 0,002 ms/ảnh, kết quả Blob trùng tool hiệu chuẩn (30,561; 33,029 mm) |
| Lần chạy đầu của job | T1 268 ms rồi 0,16 ms; T3 34 ms rồi 0,44 ms |
| `new CogImage8Grey(w, h)` | **Không xoá pixel về 0** (40/40 ảnh có rác); ảnh đều màu làm Blob lỗi "dynamic threshold" |

### A. Chạy job nào — đối tượng đã kiểm hay mở lại

| Phương án | Đánh đổi |
|---|---|
| Mở lại file khi cần chạy | Nạp hai lần (job thật vài giây/file); file có thể bị sửa giữa lúc kiểm và lúc chạy → chạy thứ chưa kiểm |
| Bộ đệm ẩn trong tầng Vision theo đường dẫn | Ai sở hữu, khi nào Dispose không rõ; file sửa thì đệm cũ |
| **`JobInspection.Job` trả chính đối tượng đã mở; `LoadedProduct` giữ job + hiệu chuẩn; lệnh "mượn" qua lease ✅** | Thứ chạy = thứ đã kiểm; chuyển sản phẩm đóng job cũ khi lệnh cuối trả lại. Đổi chữ ký Ch19 (JobInspection, ProductLoader, ProductStore) → Ch19 thành BẢN CHỤP |

### B. Hiệu chuẩn

| Phương án | Đánh đổi |
|---|---|
| Hiệu chuẩn nằm trong từng job | Mỗi sản phẩm một bản → hiệu chuẩn lại máy phải sửa mọi job |
| Chạy tool hiệu chuẩn cho mỗi ảnh | Rẻ (0,03 ms) nhưng tool có trạng thái (Input/OutputImage) → hai camera dùng chung phải khoá; sửa cả ảnh vào |
| **Lấy phép biến đổi một lần lúc nạp, `AddSpace("@", "Calibrated", …, copy)` vào ảnh và chọn space đó ✅** | Job không biết hiệu chuẩn, tool đọc mm ở space đang chọn; hỗ trợ N điểm + bàn cờ. Hợp đồng: InputImage có `@\Calibrated` đã chọn khi lệnh có hiệu chuẩn |

File: `machine\calibration\<mã>\calibration.vpp` = một `CogCalibNPointToNPointTool` hoặc `CogCalibCheckerboardTool` ĐÃ hiệu chuẩn.
Bộ nạp sản phẩm mở thật (sai kiểu / chưa hiệu chuẩn = lỗi chặn). Ch21 (máy dẫn hiệu chuẩn) sẽ ghi file này.

### C. Xếp hàng lệnh

| Phương án | Đánh đổi |
|---|---|
| Một hàng đợi chung (hai hệ tham khảo) | Hai camera nối tiếp → chu kỳ cộng dồn |
| Mỗi camera một luồng riêng + hàng đợi | Thứ tự chắc chắn; nhiều code hơn |
| **Mỗi camera một `SemaphoreSlim` (làn) + khoá theo job ✅** | Đơn giản; camera khác nhau song song, cùng camera nối tiếp; job dùng chung hai camera vẫn an toàn nhờ khoá job |

Thời hạn của lệnh tính từ lúc NHẬN (gồm xếp hàng). Quá hạn: trả Error ngay; làn + lease chỉ trả khi job thật sự xong
(không huỷ được) — lệnh sau cùng camera chờ, không chạy chồng.

### D. Kết luận

OK / NG / Error tách biệt (Error = không kết luận được, máy không được coi là NG). Reject của job → NG (thiếu số là
bình thường); Accept mà thiếu số → Error. Định dạng văn hoá bất biến; giới hạn xét trên SỐ ĐÃ LÀM TRÒN gửi máy.
Output chỉ được là số / bool / chuỗi (bộ nạp chặn kiểu khác).

### E. Chụp lại

`DeviceException.Transient`: khung hụt (CogAcqTimeout/Overrun/Missed/Timing) không đánh dấu camera hỏng; lệnh chụp
lại tối đa `retry` lần. Trước đây (Ch18) mọi lỗi → Faulted → lần chụp lại luôn gặp "chưa kết nối", `retry` vô dụng.

### Hệ quả

- Core: `Commands/` (IVisionJob, JobRun, ICalibration(+Loader), CommandResult/OutputValue/StepTimes, JobContract);
  `JobInspection.Job`; `DeviceException.Transient`.
- Vision: `CogVisionJob`, `CogCalibrationLoader`/`CogCalibration`; `CogJobInspector` giữ job; GigE báo khung hụt.
- App: `LoadedProduct`, `ProductLoader` (giữ/đóng job, nạp hiệu chuẩn, kiểu output gửi được), `ProductStore`
  (Acquire/Close, Retire sản phẩm cũ), `ResultJudge`, `CommandEngine`; `DeviceManager` bỏ qua lỗi lẻ.
- Mẫu: lệnh `T2` (vị trí đốm, hiệu chuẩn `TopCal` 0,05 mm/px gốc (10; 20)), `samples/machine/calibration/TopCal`.
- Test 54 → 74 (App: CommandEngine 8, ResultJudge 5, sản phẩm +2; Vision: CommandRunTests 5). Chạy thật sản phẩm mẫu
  qua bộ máy: lệnh đầu 93 ms (mở file ảnh + lần chạy đầu), sau đó 4–7 ms/lệnh.
- Ch18 + Ch19 là BẢN CHỤP (không tiêm lại).


---

## [20] 2026-10-25 — Giao diện v2.2: thiết kế theo tình huống sử dụng (Thiết lập lệnh, bản nháp, bộ ảnh thử)

### Bối cảnh

Tác giả: phân tích UX/UI của các phần mềm vision và các tình huống sử dụng để sửa mockup — chức năng nào đi cùng nhau
khi cài đặt; chức năng phải mở khi máy đang chạy mà cần chỉnh mẫu/tham số; thử đèn, phơi sáng và lưu tham số; tìm thêm
phần mềm vision trên GitHub. v2.1 ([15], [16]) kiểm kê "có những form gì"; lần này hỏi "làm một việc phải mở gì cùng
lúc". Bằng chứng mới: đọc mã 10 repo (VisionMix, VisionEdit, VisionPlatform.Wpf, VisionPro Tool, Battery tab,
IPC vision controller, Daheng GUI, SimpleVision40, OpenHalcon, VisionJob) + tài liệu chính thức In-Sight EasyBuilder/
Filmstrip, MERLIC Frontend, pylon Viewer. Toàn văn: `docs/ui/UX_Workflows_v2.2.md` (10 tình huống, 8 cụm chức năng).

### A. Chỗ chỉnh một lệnh

| Phương án | Đánh đổi |
|---|---|
| Form tách rời (v2.1: Camera → "ghi vào lệnh" → Lệnh & job) | Quen kiểu RefVis-C; tạo một lệnh ≥ 6 lần chuyển form; đèn/phơi sáng gắn theo camera trong khi RefVis-A gắn theo chức năng |
| Trình hướng dẫn tuyến tính | Dễ cho người mới; sửa một giới hạn cũng phải đi lại các bước |
| **Không gian "Thiết lập lệnh" theo lệnh, 4 bước đi tự do ✅** | Như In-Sight EasyBuilder. Ảnh + đèn + phơi sáng + điểm job, tham số + chạy + bộ ảnh thử ở chung cửa sổ — đúng các cụm 4 repo độc lập cùng làm. Camera lùi về cấp máy |

### B. Chỉnh khi ONLINE (chờ tác giả chốt)

| Phương án | Đánh đổi |
|---|---|
| Khoá hết khi ONLINE (v2.1, In-Sight Filmstrip, MERLIC write access) | An toàn; máy dừng cả lúc kỹ sư thử và suy nghĩ |
| **Bản nháp khi ONLINE trên ảnh đã ghi; Áp dụng nguyên khối giữa hai lệnh ✅ đề xuất** | Dựa trên cơ chế có sẵn của Ch20 (`LoadedProduct` + lease: đổi job giữa hai lệnh, lệnh đang chạy dùng job cũ tới khi xong). Live/chụp vẫn chỉ OFFLINE. Cổng áp dụng: Kỹ sư, bộ ảnh thử đã chạy, xác nhận ảnh đổi kết quả, lý do, sao lưu |
| Sửa thẳng job đang chạy | Lệnh có thể chạy trên job sửa dở — lỗi im lặng Ch20 đã đo |

### C. Các bổ sung khác

- Nguồn ảnh chung (live, chụp, 16 ảnh lệnh gần nhất trong RAM, lịch sử NG, thư mục).
- Ảnh & đèn: phơi sáng thang log, chỉ số xám/cháy sáng/độ nét/điểm job, histogram, tô vùng cháy, **chụp loạt 3×3**,
  tự chỉnh MỘT lần (như pylon) — không tự động liên tục khi chạy.
- **Tham số nhanh** = terminal input của ToolBlock khai trong `product.json` (min/max/bước) — kiểm được lúc nạp như output.
- Giới hạn đặt trên **phân bố giá trị** của bộ ảnh thử, đếm OK→NG và NG lọt.
- **Bộ ảnh thử** trong thư mục sản phẩm `samples\<lệnh>\{ok,ng}` (kiểu Good/Bad của Filmstrip).
- Đường tắt: ⚙ trên khung, dải 16 kết quả, dòng NG, ô thiết bị, ô kênh.
- Lịch sử ghi **bản sản phẩm** đã dùng (tên bản sao lưu theo mã băm, Ch19) — kiểu repo Battery lưu ảnh chụp tham số.
- Kiểm mẫu chuẩn đầu ca: đề xuất, chờ chốt.

### Hệ quả

- `docs/ui/UI_Spec_v2.md` → v2.2; mockup `meovision_ui_mockup_v2.html` → v2.2 (bản 2.1 giữ ở `meovision_ui_mockup_v2.1.html`);
  `docs/ui/UX_Workflows_v2.2.md` mới.
- Cần thêm vào code khi làm Ch22–23: `CommandConfig.Tunables` (terminal input + min/max/bước) và bộ nạp kiểm;
  vòng ảnh lệnh gần nhất trong `CommandEngine` (sự kiện `Completed` mang ảnh/record trước khi Dispose — xem Ch20 Bảng 20.3);
  bản nháp sản phẩm = thư mục làm việc + `ProductStore.Switch`; kết quả ghi tên bản sao lưu.


---

## [21] 2026-10-25 — Chốt: sửa khi ONLINE kèm chạy thử trên sản phẩm thật; bỏ kiểm mẫu chuẩn; giữ ít ảnh trong RAM

### Tác giả chốt

1. **Cho sửa khi máy đang ONLINE** — "có nhiều thay đổi nhỏ và cần kiểm tra trên sản phẩm thật trong khi dây chuyền đang
   chạy, không thể dừng rồi làm từng việc một"; tham khảo cách các phần mềm tham khảo làm.
2. **Không cần kiểm mẫu chuẩn đầu ca** — hàng chục máy mỗi ca, sản xuất ổn định thì không đáng.
3. **Giữ ảnh hợp lý** — đọc lại được từ file ảnh đã lưu.

### Cách sửa khi ONLINE

| Phương án | Đánh đổi |
|---|---|
| A. Sửa thẳng đối tượng đang trả lời máy (RefVis-A nhúng editor đầy đủ trong app sản xuất, lưu đè, không sao lưu) | Nhanh nhất; lệnh có thể chạy trên job sửa dở; không quay lại được |
| B. Bản nháp, thử trên ảnh đã ghi, áp dụng giữa hai lệnh ([20]) | An toàn; nhưng ảnh đã ghi chưa phải sản phẩm đang chạy lúc này — chưa đáp ứng "kiểm tra trên sản phẩm thật" |
| **C. B + chạy kèm bản nháp trên sản phẩm thật + quay lại một chạm ✅** | Mỗi lệnh thật trả lời bằng bản đang chạy, SAU đó cùng ảnh chạy thêm trên bản nháp (đối tượng job riêng — không dính khoá job, không chạy chồng như phép đo 9/200 ở Ch20); đếm giống/khác, giữ ảnh khác; áp dụng không bắt buộc bộ ảnh thử; quay lại bản trước từ sao lưu. Tốn thêm CPU cho chạy kèm — bận thì bỏ lượt, có bộ đếm |

### Hệ quả cho code (Ch20–23)

- `CommandEngine`: điểm móc sau khi trả lời (ảnh còn sống) để chạy kèm job nháp; ảnh chỉ Dispose sau khi chạy kèm xong
  hoặc bị bỏ lượt; không giữ làn camera trong lúc chạy kèm.
- Bản nháp = thư mục làm việc của sản phẩm + `ProductLoader` riêng; Áp dụng = `ProductStore.Save` + `Switch`; Quay lại =
  `ProductBackup.Restore` + `Switch`.
- Vòng ảnh gần nhất: mặc định 8 ảnh/lệnh trong RAM (chỉnh được, 0 = tắt); nguồn ảnh còn lại đọc thư mục ảnh đã lưu (Ch23).
- Bỏ khỏi mockup/spec: kiểm mẫu chuẩn đầu ca.


---

## [22] 2026-10-26 — Chương 21: giao tiếp với máy (TCP, PLC MC 3E, hiệu chuẩn do máy dẫn)

### Bối cảnh

Tác giả: "làm tiếp". Chương 21 = kênh nhận lệnh khai ở `machine.json` ([17]) gọi vào bộ máy chạy lệnh ([19]); PLC
Mitsubishi MC 3E trước (tác giả chốt 2026-10-16); hiệu chuẩn do máy dẫn (Ch7 §7.7.2). Ba quy tắc bản tin của Ch15
§15.1.6 (khớp nguyên mã, mã sản phẩm trong lệnh, số trường cố định) phải được code thật hoá.

### A. Một bộ xử lý lệnh cho mọi kiểu kênh

| Phương án | Đánh đổi |
|---|---|
| Mỗi kiểu kênh tự xử lý lệnh | Quy tắc (khớp mã, sản phẩm, OFFLINE, mã lỗi) viết lặp lại, dễ lệch |
| Đối tượng yêu cầu có kiểu dùng chung | Gọn cho PLC; nhưng TCP vẫn phải phân tích chuỗi riêng |
| **Dòng chữ là "ngôn ngữ chung": `CommandService.HandleAsync(kênh, dòng) → dòng` ✅** | Một chỗ cho mọi quy tắc + test không cần mạng; kênh PLC đổi thanh ghi ↔ dòng chữ (thêm vài chục dòng chuyển đổi) |

### B. Bản tin

Hỏi `T1,P-1024,SN88213`; trả lời `T1,SN88213,OK,,v1,…,vN` — mã, khoá (serial), trạng thái, lý do, rồi ĐÚNG N giá trị
(N = số output của lệnh) kể cả NG/ERR (để trống). Lý do ngắn tiếng Anh, ổn định: LIMIT/REJECT (NG); CAMERA, LIGHT,
TIMEOUT, BUSY, JOB, OUTPUT, PRODUCT, CHANNEL, UNKNOWN, NO_PRODUCT, OFFLINE, BAD_FORMAT, INTERNAL (ERR). Chỉ ASCII in
được, ≤ 256 ký tự; dấu phẩy trong giá trị chữ đổi thành `_`. Lệnh hệ thống: `HB` (trả lời cả khi OFFLINE), `SW,<sp>`.

### C. TCP: xử lý lần lượt hay song song trên một kết nối

| Phương án | Đánh đổi |
|---|---|
| Lần lượt theo thứ tự nhận | Thứ tự trả lời chắc chắn; hai camera dùng chung một kết nối phải chờ nhau |
| **Mỗi dòng một tác vụ, trả lời mang mã + serial ✅** | Hai trạm trên một kết nối chạy song song (làn camera của Ch20 vẫn giữ thứ tự từng camera); máy ghép theo mã + serial; ghi trả lời qua một khoá để không đan byte |

Nhận CR, LF hay CRLF; trả lời bằng ký tự khai `terminator`; dòng > 1024 byte → đóng kết nối; `allowedClients` cho cấu
hình hai máy tính; `NoDelay`. Đọc byte theo ISO-8859-1 để byte lạ không thành '?' hợp lệ.

### D. PLC MC 3E

Khung 3E nhị phân (đọc/ghi liên tiếp D word, M bit); kiểm byte-by-byte bằng ví dụ trong tài liệu MC của Mitsubishi
(`50 00 00 FF FF 03 00 0C 00 10 00 01 04 00 00 64 00 00 A8 03 00`). Bit đơn vị bit: 2 điểm/byte, điểm đầu 4 bit cao.
Thời hạn mỗi yêu cầu: hết hạn thì ĐÓNG kết nối (NetworkStream net48 không dừng ReadAsync theo token; khung dở cũng
làm lệch luồng byte) → kênh tự nối lại với khoảng chờ tăng dần. Vùng bắt tay mỗi kênh: M+0 Trigger (PLC), M+1
TriggerAck, M+2 InspectionComplete, M+3 ResultsValid, M+4 Online, M+5 Error, M+6 Heartbeat; D+0 mã (8 ký tự),
D+4 sản phẩm (16), D+12 serial (20), D+30 trạng thái (1/2/3), D+31 lý do (8), D+40 giá trị REAL 32 bit (2 word,
word thấp trước). Thăm dò 20 ms (MC không có đẩy sự kiện). Giá trị chữ (mã vạch) không vào được REAL → NaN; cần
kênh TCP. Chưa thử với PLC thật (máy viết sách không có) — server MC giả trong test.

### E. Hiệu chuẩn do máy dẫn

`CS,<mã>` / `CP,<mã>,<i>,<X>,<Y>` / `CE,<mã>`. `machine\calibration\<mã>\calibration.json` (camera, phơi sáng, đèn,
job tìm dấu, MinPoints, MaxRmsPixels) + job tìm dấu (`InputImage` → `X`, `Y`) nạp qua chính `IJobInspector`.
Kết quả: `CogCalibNPointToNPointTool` ghi `calibration.vpp` (đúng file Ch20 nạp); RMS vượt ngưỡng → KHÔNG ghi đè;
bản cũ vào `history`; `points.csv` có sai số từng điểm; xong thì `Switch` lại sản phẩm đang chạy để hiệu chuẩn mới
có hiệu lực giữa hai lệnh. **Đo thật:** 9 điểm → RMS 0,000 px; 4 điểm (3 thẳng hàng + 1 ngoài hàng gửi sai 1 mm) →
RMS 0,000 px vì phép biến đổi có xiên nuốt trọn điểm sai; sai ở điểm thẳng hàng → RMS 8,144 px. Vì vậy MinPoints
mặc định 9.

### F. Khởi động / tắt máy

Bước "Kênh giao tiếp" sau bước "Sản phẩm", không then chốt (cổng bị chiếm vẫn mở phần mềm, báo đỏ). Tắt máy: ngừng
nhận lệnh ĐẦU TIÊN.

### Hệ quả

- Core: `ICommandRunner` (tránh trùng tên `System.Windows.Forms.ICommandExecutor`), `Calibration/CalibrationConfig.cs`
  (+ CalibrationPoint, CalibrationFit, ICalibrationBuilder).
- App: `Comm/` (MessageProtocol, CommandService, IChannel, TcpChannel, ChannelHost, Plc/McProtocolClient, Plc/PlcChannel),
  `Calibration/CalibrationService.cs`; `MachineConfigLoader` kiểm thiết lập kênh theo kiểu.
- Vision: `Commands/CogNPointCalibrationBuilder.cs`. UI: đăng ký, bước khởi động/tắt máy, +8 khoá ngôn ngữ,
  `machine.plc.example.json`.
- Test 74 → 95 (CommandService 11, kênh/MC/PLC 6, hiệu chuẩn thật 3, PLC example 1). Chạy thật exe: TCP 2 kênh,
  T2 trung vị 6,9 ms / 50 lệnh.


---

## [23] 2026-10-26 — Chương 22: giao diện sản xuất, bản nháp, chạy kèm, áp dụng/quay lại

### Bối cảnh

Tác giả: "tiếp tục". Chương 22 hiện thực UI v2.2 ([20]) với các chốt ở [21]: sửa khi ONLINE bằng bản nháp, chạy kèm
trên sản phẩm thật, áp dụng/quay lại giữa hai lệnh, giữ ít ảnh trong RAM. Cần trả lời bằng phép đo ba câu chưa biết:
record hiển thị tốn bao nhiêu và có sống qua lần chạy sau không; hai đối tượng job chạy song song trên CÙNG một ảnh có
đúng không; ai giữ ảnh tới khi nào.

### Phép đo (spike throwaway, VisionPro 9.0 CR2 thật)

- `CreateLastRunRecord`: lần đầu 33 ms, sau đó ~0,6–0,7 ms. Record giữ đối tượng ảnh của CHÍNH lần chạy đó — chạy
  ảnh khác sau đó không ghi đè; record chỉ cần ảnh vào còn sống (Dispose ảnh → record hỏng, Ch20 Bảng 20.3).
- Hai đối tượng ToolBlock nạp từ CÙNG file .vpp chạy song song trên CÙNG một ảnh, 4 job × 300 cặp, có và không có luồng
  thứ ba đọc pixel (giả màn hình): **0 kết quả sai, 0 lỗi** (so từng bit với chạy lần lượt). Đối chiếu Ch20: MỘT đối
  tượng dùng chung hai luồng sai 9/200. ⇒ bản nháp phải là đối tượng job RIÊNG; khi đó chạy kèm an toàn mà không cần
  khoá chung với job thật.

### A. Ai giữ ảnh

| Phương án | Đánh đổi |
|---|---|
| Sự kiện `Completed` mang ảnh, người nghe tự chép | Chép 5 MB mỗi lệnh cho mỗi người nghe; record trỏ ảnh gốc nên chép không giúp gì |
| GC lo | Ảnh Cognex giữ bộ nhớ không quản lý; ca dài là hết RAM (Ch13) |
| **`DisplayFrame` đếm tham chiếu: ảnh + record, `Share()` / `ShareWith(record)` / `Dispose()` ✅** | Một ảnh, nhiều người giữ (khung màn chính, vòng 8 ảnh, chạy kèm, form Thiết lập); ảnh trả khi người cuối trả. Bộ máy chạy lệnh trả bản của nó ngay sau `Completed` — ai cần giữ phải `Share` TRONG sự kiện |

### B. Tham số nhanh

Terminal INPUT của ToolBlock khai trong `product.json` (`tunables`: terminal, tên, đơn vị, min, max, bước, giá trị).
Bộ nạp kiểm: terminal có thật, kiểu số, khoảng hợp lệ, giá trị trong khoảng, không trùng, không phải `InputImage`.
`CogVisionJob.Run(image, JobRequest)` đặt giá trị bằng `Convert.ChangeType` theo kiểu terminal (đo: 20.0 → int).
Sửa số không phải lưu lại .vpp.

### C. Bản nháp và chạy kèm

- Bản nháp = chép `product.json` + `jobs` sang `drafts\<mã>` rồi nạp bằng chính `ProductLoader` → job đối tượng riêng;
  sửa số qua `DraftSession.Set*` (khoá; chạy lấy bản chụp cấu hình).
- Chạy kèm (`ShadowRunner`): sau mỗi lệnh thật, nếu lệnh ĐÃ SỬA trong bản nháp thì chạy job nháp trên cùng ảnh ở luồng
  nền; bận thì bỏ lượt (đếm); editor VisionPro đang mở thì bỏ lượt (editor sửa ToolBlock không qua khoá job). Lệnh chưa
  sửa KHÔNG chạy kèm — chạy thật cho thấy chạy kèm cả lệnh chưa sửa làm 30/102 lượt của lệnh đáng so bị bỏ.
- Phơi sáng/đèn: chạy kèm không thử được (ảnh chụp bằng thiết lập đang chạy) — form cảnh báo khi bản nháp đổi chúng.

### D. Áp dụng / quay lại

`ProductStore.Apply`: ghi job đã sửa + product.json vào thư mục nháp → sao lưu sản phẩm (lý do vào tên bản sao lưu)
→ chép sang → `Switch` (nạp và kiểm lại TỪ ĐĨA; lệnh đang chạy dùng job cũ tới khi xong — lease Ch20). Kiểm không qua
→ khôi phục bản sao lưu vừa tạo; sản phẩm cũ chưa từng ngừng. `RollBack`: sao lưu bản hiện tại → `Restore` → `Switch`
(quay lại cũng quay lại được). Từ chối nếu máy đã chuyển sang sản phẩm khác trong lúc sửa.

**Lỗi tìm được khi chạy thật:** `ProductStore` ghi `product.json` bằng cài đặt mặc định của Newtonsoft → PascalCase,
`null` khắp nơi, BOM — khác hẳn file người viết tay (lỗi có từ `Save` của Ch19, chưa từng chạy). Sửa: camelCase, bỏ
null, UTF-8 không BOM; test khẳng định.

### E. Đóng job cũ ngoài khoá (tìm ra khi chạy thật)

Lúc Áp dụng, một lệnh `T1` mất 542 ms khứ hồi dù bên trong bộ máy chỉ 12 ms: lệnh chờ khoá của `ProductStore`
trong lúc `SetCurrent` cho sản phẩm cũ nghỉ — đóng job NGAY trong khoá (636 ms giữa "nạp xong" và "đã đổi").
Đo (spike): đóng ToolBlock vừa nạp 1–7 ms; Blob đã chạy 2 000 lần có tạo record mỗi lần: 179 ms; không tạo record:
19 ms. Record làm job Blob giữ thêm ~220 MB (ổn định ~290 MB suốt 24 000 lần — có trần, không rò; trả khi Dispose);
job Histogram không. Sửa: thay sản phẩm trong khoá, cho nghỉ NGOÀI khoá (an toàn: sau khi thay, Acquire chỉ thấy bản
mới; ai đã mượn bản cũ giữ lease). Đo với `SW` mỗi 3 s giữa ~1 000 lệnh: lớn nhất 236 → 118 ms (bằng lệnh ảnh lớn
bình thường). `CreateDisplayRecords` ghi rõ chi phí cả RAM, không chỉ thời gian.

### F. Giao diện

- Màn chính: thanh công cụ lớn, lưới khung theo lệnh (`ResultFrameView`: `CogRecordsDisplay` không combobox/thanh cuộn,
  thẻ OK/NG/ERR, lý do, dải 16 kết quả — 8 mới nhất còn ảnh), dock Nhật ký | Bộ đếm, ONLINE nhấn giữ 1 s, khởi động hỏng
  bước then chốt → OFFLINE. Khung chỉ vẽ kết quả MỚI NHẤT (gộp lần vẽ), không bỏ dòng nào trên dải.
- `SetupForm` (không modal): lệnh · nguồn ảnh (lệnh gần nhất, ảnh khác của chạy kèm, thư mục) · bản nháp cạnh bản đang
  chạy · tab Job (thanh trượt tham số nhanh, editor VisionPro trên job nháp — ẩn nút mở/lưu, `Font = DefaultFont`) /
  Kết quả & giới hạn / Ảnh & đèn / Bộ ảnh thử · Chạy kèm / Khác biệt / Huỷ / Quay lại / Áp dụng (hỏi lý do).
- Quyền (chỉ Kỹ sư được sửa) để Chương 23.

### Hệ quả

- Core: `DisplayFrame`, `JobRequest`, `IVisionJob.Run(image, request)` / `Save` / `EditSubject`, `JobRun.Record`,
  `CommandResult.Frame`, `TunableConfig`.
- App: `CommandEngine` (record + tham số nhanh, trả khung sau `Completed`), `RecentFrames`, `ShadowRunner`,
  `DraftSession`, `ProductStore.OpenDraft/Apply/RollBack`, `ProductLoader.CheckTunables`, `AppPaths.Drafts`,
  `AppSettings.RecentFramesPerCommand/CreateDisplayRecords`.
- Vision: `CogVisionJob` (đặt input, record trong khoá, Save file tạm + đổi tên). UI: `MainForm` viết lại,
  `Display/ResultFrameView`, `Display/RecordView`, `Setup/SetupForm`, `Setup/JobEditorForm`, `Setup/ReasonDialog`;
  +98 khoá ngôn ngữ (13 kiểm/khác biệt, 85 giao diện); bước tắt máy "Trả ảnh đang giữ".
- Job mẫu T2/T3 có input `MinArea` (tham số nhanh). Test 95 → 111. Chạy thật exe: 3 lệnh liên tục qua TCP; kéo
  MinArea 20 → 300 trên bản nháp; chạy kèm Giống 0 · Khác 43 · Bỏ lượt 0; Áp dụng giữa dòng lệnh → T3 chuyển OK; Quay lại
  → product.json đúng bản gốc từng byte; editor VisionPro mở trên job nháp; tắt máy đúng thứ tự.


---

## [24] 2026-10-28 — Chương 23: người dùng, phân quyền, nhật ký thay đổi, lưu kết quả/ảnh xoay vòng

### Bối cảnh

Tác giả: "tiếp tục làm". Chương 23 = phần còn thiếu để MeoVision dùng được trong sản xuất: ai được làm gì (UI_Spec
v2.2 §6: Vận hành / Kỹ sư / Quản trị), ai đã đổi gì (tình huống S8: khách khiếu nại một serial — lệnh đó chạy với job
và giới hạn nào?), và kết quả + ảnh nằm ở đâu, giữ bao lâu. Chính sách bảo mật theo đúng bối cảnh nhà máy tác giả đã
chốt cho AM.AutoFrame: không khoá tài khoản, không ép đổi mật khẩu, phải có đường khôi phục quyền Quản trị và đường đó
phải ồn ào.

### A. Tài khoản

| Phương án | Đánh đổi |
|---|---|
| Tài khoản từng người, bắt buộc | Truy vết chính xác; nhưng ca đêm dùng chung máy, người mới chưa có tài khoản → máy chờ |
| **Tài khoản theo vai (engineer, admin) + được thêm tài khoản tên riêng ✅** | Đúng thực tế xưởng; nhật ký ghi tên tài khoản. Vận hành không mật khẩu |
| Đăng nhập Windows (AD) | Không có AD ở nhiều xưởng; IPC thường một tài khoản Windows tự đăng nhập |

Mật khẩu: PBKDF2-SHA256, salt 16 byte, 100 000 vòng (`Rfc2898DeriveBytes`, net48 ≥ 4.7.2), so sánh không lộ thời gian.
Lần chạy đầu tạo `engineer`/`admin` với mật khẩu cài sẵn — **nhắc** trên thanh trạng thái, không ép đổi. Sai mật khẩu:
chỉ ghi nhật ký, không đếm, không chờ, không khoá.

### B. Khôi phục quyền Quản trị

| Phương án | Đánh đổi |
|---|---|
| Mã theo ngày từ nhà cung cấp (HMAC ngày + khoá máy) | Cần công cụ + giữ khoá bí mật; người giữ có thể không còn |
| **File khôi phục `recover-admin.txt` trong thư mục dữ liệu, dùng một lần ✅** | Ai đặt được file vào ProgramData đã là quản trị Windows của máy — không mở thêm cửa nào. Đọc dòng đầu → đặt cho mọi tài khoản Quản trị → xoá file (kể cả khi không hợp lệ) → nhật ký `admin-recovered` + bước khởi động vàng + dải đỏ trên màn chính |
| Sửa tay users.json | Dễ hỏng file (JSON, base64) → cả ca không ai đăng nhập được |

> **Đính chính (2026-10-29, xem [25] mục F):** "ai đặt được file vào ProgramData đã là quản trị Windows" là SAI —
> `BUILTIN\Users` có quyền tạo file trong thư mục dữ liệu. Từ Ch24 chỉ nhận file khôi phục có chủ Administrators/SYSTEM.

### C. Tự đăng xuất

Kỹ sư/Quản trị về Vận hành sau `IdleLogoutMinutes` (mặc định 10) không thao tác. Form Thiết lập lệnh đang mở thì
**khoá phần sửa, giữ bản nháp** (đóng form = mất việc đang dở; để nguyên = cửa sổ có quyền sửa job không người trông).
**Tìm ra khi chạy thật:** để yên 75 giây (cài 1 phút trong bản build) mà vẫn chưa đăng xuất — đồng hồ bị làm mới vì
chuột của người dùng lướt qua cửa sổ MeoVision trong lúc họ làm việc ở chương trình khác (đo: con trỏ đổi vị trí trong 5
giây, đang nằm trên cửa sổ MeoVision; nhật ký ghi `logout idle` muộn hơn, lúc 23:14:50). Bộ lọc nay chỉ tính khi một
cửa sổ MeoVision đang được chọn, và với chuột chỉ khi con trỏ thật sự đổi vị trí (phòng thêm trường hợp Windows gửi
`WM_MOUSEMOVE` khi control dưới con trỏ được tạo lại — chưa đo, chỉ phòng). Đo lại: đăng nhập 23:19:10, `logout idle`
23:20:46.

### D. Quyền

Một bảng `Permissions` (Core) — nút, lệnh, test đều hỏi ở đó. Nút thiếu quyền vẫn hiện, có ổ khoá, bấm thì nói cần vai
trò nào (UI_Spec §7.1). Vận hành không tắt được phần mềm (đóng cửa sổ); tắt Windows vẫn được.

### E. Nhật ký thay đổi

| Phương án | Đánh đổi |
|---|---|
| Bảng trong SQLite | Truy vấn dễ; thêm phụ thuộc; một file hỏng mất cả lịch sử |
| **JSON Lines theo tháng (`audit\yyyy-MM.jsonl`), mở–ghi–đóng mỗi lần ✅** | Đọc được bằng Notepad, grep, Excel (Power Query); dòng hỏng chỉ mất dòng đó; vài chục dòng/ca không đáng giữ file mở |

Ghi: `app-start/stop`, `login`, `login-failed`, `logout` (manual/idle), `password-changed`, `admin-recovered`,
`online/offline` (nút/khởi động), `product-loaded` (mọi lần đổi sản phẩm/phiên bản, kể cả do máy gửi `SW`),
`apply/rollback` (lý do, danh sách thay đổi, bản sao lưu trước, phiên bản mới).

### F. Phiên bản sản phẩm trong từng kết quả

Phiên bản = 12 ký tự đầu SHA-256 trên danh sách "file:mã băm" của đúng tập file mà bản sao lưu chụp (product.json,
jobs, golden). Gắn vào `LoadedProduct` lúc nạp và vào MỌI `CommandResult` (cùng serial, kênh, lúc nhận). Lịch sử đi
ngược: serial → phiên bản → dòng `product-loaded` → dòng `apply` (ai, lý do, đổi gì). Đã chạy thật trên giao diện.

### G. Kết quả và ảnh

| Phương án | Đánh đổi |
|---|---|
| SQLite một bảng | Truy vấn nhanh; QA không mở được bằng Excel; cột giá trị đổi theo lệnh khó |
| **CSV theo ngày × lệnh (`results\yyyy-MM-dd\T3.csv`), mỗi output một cột ✅** | Excel mở được; tiêu đề đổi (sau áp dụng) → file mới `T3_2.csv`; giá trị theo văn hoá bất biến (Excel tiếng Việt cần nhập với dấu chấm thập phân) |
| JSON Lines | Máy đọc dễ; người không đọc được |

Ảnh: BMP qua `CogImageFile` (không mất dữ liệu, camera đọc thư mục của Ch18 đọc lại được; đo 5 MP: trung vị 32 ms, lớn
nhất 115 ms, 5,0 MB). Mặc định lưu ảnh NG/lỗi, ảnh OK mỗi N lệnh (0 = không). Ghi trên một luồng nền; ảnh chờ ghi tối đa
16, đầy thì bỏ ảnh (đếm) nhưng vẫn ghi dòng. File ảnh ghi ra tên tạm rồi đổi tên.
**Tìm ra khi chạy thật:** mở form Lịch sử đúng lúc ghi làm mất một dòng — `File.ReadLines` mở chặn người ghi. Sửa: đọc
với `FileShare.ReadWrite`; bộ ghi thử lại, file vẫn bị khoá (QA mở bằng Excel giữ khoá cả buổi) thì ghi sang file kế
tiếp. Nhật ký thay đổi sửa cùng cách (`yyyy-MM_2.jsonl`). Test mô phỏng Excel giữ khoá: không mất dòng nào.

### H. Xoay vòng

Theo thư mục ngày: ảnh quá `KeepImageDays` (30), kết quả quá `KeepResultDays` (365) thì xoá; ổ ảnh dưới `ImageMinFreeGb`
(30) thì xoá thêm ngày ảnh cũ nhất; không bao giờ ngày hôm nay, không bao giờ thư mục không đúng dạng ngày. Chạy lúc
khởi động (bước "Lưu trữ") và mỗi giờ. Đo: ba lần chạy thử ~15 phút với ảnh mẫu (tỷ lệ NG cao) → 1 003 ảnh, 2,0 GB.

### Hệ quả

- Core: `Security/Permissions.cs` (UserRole, Permission, bảng quyền), `Storage/IImageStore.cs`; `CommandResult` thêm
  Channel/Serial/ReceivedAt/ProductId/ProductVersion; `ICommandRunner.ExecuteAsync(channel, code, serial, ct)`.
- App: `Security/UserStore.cs`, `Security/Session.cs`, `Audit/AuditLog.cs`, `Results/ResultRecorder.cs`,
  `Results/ResultReader.cs`, `Results/StorageJanitor.cs`; `ProductBackup.ContentVersion`, `LoadedProduct.Version`,
  `ProductStore.Switched/CurrentVersion`; `CommandEngine` gắn truy vết; `CommandService` chuyển serial;
  `AppSettings.IdleLogoutMinutes/Results/Retention`.
- Vision: `Storage/CogImageStore.cs`. UI: `Security/LoginForm.cs`, `Security/InputActivityFilter.cs`,
  `History/HistoryForm.cs`; MainForm (Lịch sử, Đăng nhập, ổ khoá, ô người dùng + đếm ngược, cảnh báo); SetupForm (khoá
  khi về Vận hành, nhật ký áp dụng/quay lại); Program (bước "Người dùng", "Lưu trữ", "Ghi nốt kết quả", dọn mỗi giờ).
- Test 111 → 130 (UsersAndDataTests 17, ImageStoreTests 2 chạy thật VisionPro). Chạy thật exe ba lần (giao diện tiếng
  Việt, điều khiển bằng UIA/PostMessage): khoá theo vai, đăng nhập, áp dụng có lý do, tự đăng xuất, lịch sử truy ngược
  phiên bản; 1 986 + 1 529 + 1 218 dòng kết quả, 0 ảnh bị bỏ.

## [25] 2026-10-29 — Chương 24: triển khai và bảo trì (cài máy mới, sao lưu/khôi phục trạm, kiểm hồi quy, gói chẩn đoán)

### Bối cảnh

Tác giả: "tiếp tục làm Chương 24". Lộ trình [9] ghi cho Ch24: checklist cài máy mới, đổi bản VisionPro, sao lưu/khôi
phục, replay hồi quy, gói chẩn đoán. Ch16 đã dạy ý tưởng (replay theo đúng thứ tự chụp, nhãn do chính phần mềm phân loại
không phải ground truth, gói chẩn đoán mặc định hẹp — Code 16.3 chỉ là minh hoạ); Ch13 §13.1.3 đã dạy bindingRedirect.
Ch24 xây thật, không dạy lại.

**Phát hiện khi khảo sát (sai ở Ch23):** DESIGN_LOG [24] mục B và sách Bảng 23.2 nói "ai đặt được file vào ProgramData
đã là quản trị Windows". SAI với quyền mặc định: `icacls C:\ProgramData\MeoVision` cho `BUILTIN\Users:(CI)(WD,AD,WEA,WA)`
— mọi tài khoản Windows thường TẠO được file mới trong thư mục dữ liệu, kể cả `recover-admin.txt`. Sửa ở mục F.

### A. Phát hành và cài máy mới

| Phương án | Đánh đổi |
|---|---|
| MSI (WiX) / Inno Setup | Quen thuộc, gỡ cài sạch; thêm một dự án phải bảo trì; cài đè một bản → khó chạy lại bản cũ |
| ClickOnce | Tự cập nhật; cài vào hồ sơ người dùng, cần quyền mạng, không hợp máy xưởng |
| **Thư mục theo phiên bản (`C:\MeoVision\2.0.261029\`) + `release.json` (mã băm từng file) ✅** | Chép là cài; nhiều bản nằm cạnh nhau → quay lại bản cũ = chạy exe cũ (dữ liệu ở `DataRoot` không đổi, Ch17); không có trình gỡ cài |

Số phiên bản theo quy ước của tác giả: `2.0.YYMMDD`. **Đo thật:** `/p:Version=2.0.261029` → lỗi CS7034/CS7035 — mỗi phần
của AssemblyVersion/FileVersion ≤ 65535. Ngày build chỉ vào `InformationalVersion`; AssemblyVersion giữ `2.0.0.0`.
`release.json` do chính exe ghi (`--write-release`) để chỉ có MỘT chỗ tính mã băm: phiên bản, lúc build, bản VisionPro lúc
build (`CogMisc.VisionProReleaseNumber`), danh sách file + SHA-256. `tools/publish.ps1`: build Release → chép → ghi
release.json.

### B. Kiểm cài đặt `MeoVision.exe --check`

| Phương án | Đánh đổi |
|---|---|
| Checklist giấy | Không tốn code; người cài bỏ sót, không có bằng chứng |
| Công cụ kiểm riêng | Hai chỗ biết cách khởi động → lệch nhau |
| **Chạy đúng chuỗi khởi động của chương trình, không mở giao diện, ghi báo cáo + mã thoát ✅** | Kiểm đúng cái sẽ chạy thật (VisionPro, license, machine.json, camera theo serial, sản phẩm, cổng); thêm bước kiểm file chương trình và tài khoản |

Bước mới (cả khi chạy thường, không then chốt): `programFiles` (file khớp release.json; bản VisionPro khác bản lúc build →
cảnh báo, trỏ Ch13 §13.1.3); `account` (đang chạy bằng quyền Administrator → cảnh báo). Mã thoát 0 sẵn sàng / 1 có cảnh báo
/ 2 bị chặn. Báo cáo `diagnostics\check-yyyyMMdd-HHmmss.txt`, in ra cửa sổ lệnh (AttachConsole).

### C. Sao lưu / khôi phục cả trạm

| Phương án | Đánh đổi |
|---|---|
| Ảnh đĩa (image cả ổ) | Khôi phục được mọi thứ; cần công cụ, chục GB, không sang được máy khác phần cứng |
| Chép tay thư mục dữ liệu | Không có kiểm; chép kèm 30 ngày ảnh; quên file |
| **Một file zip cấu hình trạm + manifest mã băm ✅** | Vài MB; khôi phục kiểm băm TRƯỚC khi ghi; không chứa ảnh/kết quả/log (dữ liệu sản xuất, không cần để dựng lại trạm) |

Gồm: machine.json, `machine\calibration`, `products` (bản hiện hành, không gồm sao lưu/bản nháp), active-product.txt,
users.json (mã băm PBKDF2 — file sao lưu phải cất như tài liệu mật), appsettings.json của chương trình. Khôi phục: chỉ
bằng dòng lệnh `--restore` (chương trình không chạy — mutex), cấu hình cũ chuyển sang `backup\station-before-restore-*`,
nhật ký `station-restored`. Sao lưu: nút cho Quản trị + `--backup`.

### D. Kiểm hồi quy trước khi áp dụng

| Phương án | Đánh đổi |
|---|---|
| Chạy lại ảnh đã lưu, so với kết quả máy đã trả ✅ | Có sẵn (Ch23 lưu ảnh + kết quả + phiên bản); so "khác trước" chứ không so "đúng"; ảnh lưu lệch về NG |
| Bộ ảnh có nhãn người gán (`samples\<lệnh>\OK|NG`) ✅ | Ground truth độc lập (Ch16 §16.3.4); phải có người gom và gán nhãn |
| Chạy cả bản đang chạy lẫn bản nháp trên mỗi ảnh | So được cả ảnh của phiên bản cũ; job đang chạy thuộc về lệnh của máy — chạy thêm làm chậm máy |

Làm cả hai cái đầu. Chạy lại: chỉ dòng có ảnh, cùng mã lệnh, **cùng phiên bản đang chạy**, theo ĐÚNG thứ tự thời gian
(Ch16: script có trạng thái), trên MỘT job nháp; so trạng thái + chữ của từng output (như chạy kèm). Kết quả (số ảnh,
khác, bỏ qua) vào hộp Áp dụng và vào nhật ký `apply` (`replay`). Không chặn áp dụng — chỉ ghi lại đã kiểm hay chưa.

### E. Gói chẩn đoán

Theo Ch16 §16.4.5, mặc định hẹp: ghi chú của người xuất, `system.txt` (phiên bản MeoVision/VisionPro, license, Windows,
đĩa, sản phẩm + phiên bản, thiết bị), machine.json, appsettings.json, sản phẩm đang chạy, log 2 ngày gần nhất, nhật ký
thay đổi tháng này, file sự cố, CSV kết quả 2 ngày, 20 ảnh NG gần nhất. Mở rộng tuỳ chọn: mọi sản phẩm, số ngày, số ảnh.
KHÔNG gồm users.json. Ghi vào thư mục cố định `diagnostics\`. Vận hành được xuất (họ là người thấy sự cố).

### F. Sửa lỗ hổng file khôi phục (Ch23)

| Phương án | Đánh đổi |
|---|---|
| Đặt file trong thư mục chỉ Administrators ghi được | Chương trình (tài khoản thường) không xoá được → hết "dùng một lần" |
| Bỏ file khôi phục, dùng mã theo ngày | Quay lại vấn đề người giữ khoá |
| **Chỉ nhận file có CHỦ là `BUILTIN\Administrators` hoặc `SYSTEM` ✅** | File tạo từ cửa sổ lệnh "Run as administrator" có chủ là nhóm Administrators (đối chiếu thật trên máy: thư mục do trình cài chạy nâng quyền tạo có chủ Administrators, do phiên thường tạo có chủ là người dùng). File của tài khoản thường → từ chối, xoá, nhật ký `admin-recovery-rejected` |

**Giới hạn phải nói thẳng:** trên IPC một tài khoản Windows tự đăng nhập, người ngồi trước máy chính là tài khoản ghi
được cả thư mục dữ liệu — sửa được product.json, chép users.json từ máy khác. Mật khẩu MeoVision chặn thao tác qua giao
diện, không chặn người có quyền vào Windows Explorer. Chống điều đó là việc triển khai: tài khoản tự đăng nhập là tài
khoản thường, khoá desktop (kiosk / Shell Launcher) — vào checklist cài máy mới, `--check` cảnh báo khi chạy bằng quyền
Administrator.

### Kết quả chạy thật (2026-10-29, bản phát hành 2.0.261029)

- `tools/publish.ps1`: build Release, chép `dist\MeoVision-2.0.261029\`, exe ghi release.json (43 file, VisionPro 9.0 CR2).
- `--check` trên máy dev: 11 s, mã thoát 1 — cảnh báo đúng: tài khoản Windows là quản trị, đèn MainLight chưa nối, một
  file `.vpp` thừa trong sản phẩm (sót từ Ch19), mật khẩu cài sẵn. Chép đè `MeoVision.App.dll` bằng bản Debug → bước "File
  chương trình" báo "MeoVision.App.dll đã bị thay".
- File khôi phục do phiên KHÔNG nâng quyền tạo (chủ `NHAN\dinhn`, tài khoản thuộc nhóm Administrators) → từ chối, xoá,
  nhật ký `admin-recovery-rejected`. Nhánh chấp nhận (file tạo từ cửa sổ "Run as administrator") chỉ kiểm bằng test có
  chủ file giả — cần một lần thử tay có nâng quyền.
- Sao lưu trạm `--backup`: 10 file, 19,8 KB, 1 s. Khôi phục sang "máy mới" (bản phát hành chép ra chỗ khác, DataRoot rỗng)
  → `--check` SẴN SÀNG CHẠY TỰ ĐỘNG. Lộ ra: khôi phục vào thư mục rỗng vẫn tạo thư mục "cấu hình cũ" rỗng → sửa.
- Bộ ảnh có nhãn (dựng bằng `samples/products/make_labelled_set.py`, nhãn theo cách vẽ: đốm ~254 px là lỗi, bụi ~30 px
  không) — MinArea 20: nhận nhầm 0, loại nhầm 4; **MinArea 300 (giá trị đã áp dụng ở Ch23): nhận nhầm 5/5**; MinArea 100:
  0 / 1. Ảnh sạch hoàn toàn → LỖI (Blob trên ảnh đều màu, như Ch20). Lộ ra: `image.Source` của camera đọc thư mục chỉ là
  tên file → nhãn theo thư mục cha mất → sửa (ghép lại thư mục).
- Chạy lại ảnh đã lưu T3: bản nháp = bản đang chạy → 289 ảnh, giống 289, khác 0, 3,5 s (ảnh BMP lưu + chạy lại tái tạo đúng
  kết quả lúc chạy thật); MinArea 300 → khác 289/289, 1,35 s; hộp Áp dụng hiện dòng chạy lại.
- Gói chẩn đoán: 46 mục, 13,4 MB (ảnh NG 56 MB → nén 13 MB), ~1 s. Lộ ra: `Environment.OSVersion` báo "Windows NT
  6.2.9200" trên Windows 11 (exe không khai manifest tương thích) → dùng `RuntimeInformation.OSDescription`. Ghi chú bị
  cắt ở dấu ":" là do script điều khiển, không phải chương trình.
- Đo thật thêm: `CamelCasePropertyNamesContractResolver` đổi cả KHOÁ dictionary ("MeoVision.App.dll" → "meoVision.App.dll")
  → dùng `CamelCaseNamingStrategy { ProcessDictionaryKeys = false }` cho release.json và station.json.
- Tự đăng xuất lúc 08:10:43 dù script vẫn bấm: thao tác gửi bằng PostMessage khi cửa sổ không được chọn không tính — đúng
  quy tắc của Ch23.

### Hệ quả

- Core: `Permission.ExportDiagnostics` (Vận hành), `Permission.BackupStation` (Quản trị).
- App: `Hosting/ReleaseManifest.cs`, `Hosting/InstallChecks.cs`, `Commands/ReplayRunner.cs`,
  `Maintenance/StationBackup.cs`, `Maintenance/DiagnosticBundle.cs`; `UserStore.ApplyRecovery` trả `RecoveryResult`, chỉ
  nhận file chủ Administrators/SYSTEM; `ProductBackup.Sha256Of/Relative` thành internal.
- UI: `Maintenance/WindowsAccount.cs`, `Maintenance/DiagnosticForm.cs`; MainForm menu Bảo trì, phiên bản 2.0.YYMMDD; SetupForm
  bộ ảnh có nhãn (OK\ NG\), "Chạy lại ảnh đã lưu", dòng chạy lại trong hộp Áp dụng + nhật ký `replay`; Program: dòng lệnh
  `--check`, `--write-release`, `--backup`, `--restore`, bước "Tài khoản Windows", "File chương trình"; +40 khoá ngôn ngữ.
- `tools/publish.ps1`, `samples/products/make_labelled_set.py` + `samples/products/P-1024/samples/T3/{OK,NG}`.
- Test 130 → **140** (MaintenanceTests 9, DraftTests +1 chạy lại).


## [26] 2026-10-30 — Đối chiếu cấu hình với RefVis-A/C, thanh nút theo mockup v2.2, lỗi bố cục khung (ĐÃ LÀM — Session 23)

### Bối cảnh

Tác giả: đọc chi tiết file cấu hình/ini/product của các dự án tham khảo, so với MeoVision, bổ sung phần thiếu; sửa các
nút theo mockup v2.2; các khung hiện lên bị lỗi (không phủ hết màn hình, bị kéo lên trên); tham khảo cách các chương
trình đặt file tool và cấu hình trong thư mục chương trình.

Đã đọc: RefVis-A `PublicSystem.ini` (hệ thống), `Products\<n>.<tên>\AppConfig.ini` (sản phẩm, có cả khối JSON),
`DataDB\InputIO_Param.csv` (bảng IO card); RefVis-C `project\main.ini` (máy), `project\<dự án>\config.ini` (lệnh +
vị trí chuẩn), `data.xml`/`offsetdata.xml` (rỗng — nhóm dữ liệu, bù/giới hạn theo output).

### A. Cấu hình — điểm chung của hai chương trình thật, so với MeoVision

| Mục | RefVis-A | RefVis-C | MeoVision hiện có | Đề xuất |
|---|---|---|---|---|
| Nhiều camera trong MỘT lệnh | `FunCameraPRMs`: mỗi camera đổ vào một terminal (`InputImage1/2`) | — | 1 camera/lệnh | **Thêm** `cameras[]` (vai trò → terminal) |
| Thời gian chờ đèn ổn định | `LightDatas.Timems` | — | không | **Thêm** `settleMs` cho mỗi đèn |
| Brightness/Contrast (gain) camera | theo lệnh | theo lệnh, cả Gamma | chỉ ExposureMs | **Thêm** `brightness`, `contrast` (tuỳ chọn) |
| Lệnh chọn khung hiển thị | `DisplayID`, `RecordID` | `InspectDisplay`, `NumOfDisplays`, `MaxColumns` | mỗi lệnh 1 khung, lưới tự tính | **Thêm** `display.slot` + bố cục khung trong machine.json |
| Lệnh ghép từ kết quả lệnh khác | — | `CmdType=ComposeCmd`, `InspectInput=T21,SN` | không | Đề xuất sau (thiết kế riêng) |
| Bật/tắt hiện kết quả, dữ liệu | — | `ShowResult`, `ShowData` | luôn hiện | nhỏ — tuỳ chọn |
| Thử lại, thời hạn | — | `RetryTimes`, `TimeOut` | có (`retry`, `timeoutMs`) | đủ |
| Tách chụp khỏi xử lý | — | `IsSnapAndProcessSeparated` | không | Đề xuất sau (trả lời máy ngay sau khi chụp) |
| Vị trí chuẩn (golden) có cấu hình riêng | `PosX/Y/A/Z` theo lệnh | `GoldenPosSetup` (camera, hiệu chuẩn, phơi sáng) | thư mục `golden` | để Ch7/teach |
| Hiệu chuẩn: tâm xoay, chế độ tương đối | `RotationX/Y` | `CalibMode=Relatively`, `CalibMotion` | file .vpp hiệu chuẩn | để sau |
| Lưu ảnh: gốc + ảnh có đồ hoạ | `OrgImage`, `CutImage`, ok/ng riêng, ngày giữ riêng | `SaveRawType`, `SaveWindowType`, mẫu tên file | ảnh gốc NG, OK mỗi N | **Thêm** lưu ảnh có đồ hoạ (record) + mẫu tên |
| Thư mục ảnh/dữ liệu ở ổ khác | `E:\Image`, `E:\TestData` | `D:\Image`, `D:\Data` | trong DataRoot | **Thêm** `ImagesRoot`, `ResultsRoot` |
| Bộ đếm giữ qua lần mở máy | `[Count] PASS/FAIL` | — | mất khi tắt | **Thêm** lưu bộ đếm |
| Ký tự kết thúc bản tin theo cổng | — | `ServerEndChar` | cố định CRLF | **Thêm** `terminator` theo kênh |
| Kênh client (MES/PDCA) | `MesServerIP/Port` | `ClientSetup` | chỉ server | Đề xuất sau |
| Toàn màn hình, ẩn menu | — | `FullScreen`, `ShowMenu` | không | **Thêm** `FullScreen` |
| Mật khẩu | `[SavePw]` chữ thường trong ini | — | PBKDF2 | MeoVision tốt hơn — giữ |
| Đèn: hãng, cổng COM, baud | `Light_1 Model/IP_Com` | `LightController Vendor/COM/Baud` | có (AsciiLight Serial/TCP) | đủ |

### B. Vị trí file tool và cấu hình

Cả hai chương trình đặt sản phẩm NGAY TRONG thư mục chương trình: RefVis-A `bin\Debug\Products\<n>.<tên>\{AppConfig.ini,
ToolBlock\*.vpp, CalibrationTool\*.vpp}` + `PublicSystem.ini` cạnh exe; RefVis-C `exe\project\main.ini` +
`exe\project\<dự án>\{config.ini, Setup\Calib|GoldenPos|Inspect\*.vpp}`. Ưu: một thư mục là cả trạm, dễ tìm, chép là
xong. Nhược đo được ngay trên hai thư mục: bản sao "副本"/"测试" của .vpp nằm lẫn, sản phẩm đã xoá đổi tên "已删除" vẫn
nằm đó, ảnh/log ghi vào ổ khác bằng đường dẫn tuyệt đối; nâng cấp chương trình = chép đè lên chính dữ liệu.

| Phương án | Đánh đổi |
|---|---|
| (a) Giữ `%ProgramData%\MeoVision` (hiện tại, Ch17) | Tách sạch chương trình/dữ liệu, nâng cấp an toàn; người hiện trường khó tìm (thư mục ẩn) |
| (b) Dữ liệu trong thư mục exe như hai chương trình tham khảo | Dễ tìm; nhưng mỗi bản phát hành một thư mục (Ch24) → dữ liệu bị tách theo phiên bản; Program Files không ghi được |
| (c) **Thư mục trạm** `C:\MeoVision\` chứa `Data\` + các bản `2.0.YYMMDD\` cạnh nhau, `DataRoot = ..\Data` | Dễ tìm như (b), nâng cấp an toàn như (a); bố cục bên trong `Data\products\<mã>\{product.json, jobs, golden, samples}` vẫn như cũ |

### C. Bố cục khung (lỗi)

- Kích thước form cố định: MainForm 1600×950, SetupForm 1560×900, HistoryForm 1500×880, JobEditorForm 1400×860. Màn
  1920×1080 ở 100% → MainForm không phủ hết; ở 125% (vùng làm việc logic ~1536×826) → SetupForm/HistoryForm lớn hơn màn,
  căn giữa làm mép trên ra ngoài màn hình. Sửa: MainForm mở phóng to; form con lấy kích thước theo `Screen.WorkingArea`.
- Lưới khung ảnh: 3 lệnh → 2×2, một ô trống. Sửa: bố cục theo `display` trong machine.json (số khung, số cột), mặc
  định ≤3 khung một hàng; lệnh chọn khung.

### D. Thanh nút theo mockup v2.2

Mockup: Trang chính · Camera & đèn · Hiệu chuẩn · Thiết lập lệnh · Giao tiếp · Sản phẩm · Lịch sử · Cài đặt | Đăng
nhập · Thoát; nút lớn có biểu tượng, khoá = mờ + ổ khoá + nói lý do; phải: sản phẩm, ngôn ngữ, ONLINE nhấn giữ. Hiện có:
Thiết lập lệnh, Chụp thử, Lịch sử, Đặt lại bộ đếm, Đăng nhập (+ menu). Form chưa có: Camera & đèn, Hiệu chuẩn, Giao
tiếp, Sản phẩm, Cài đặt.

### Tác giả chốt (2026-10-30)

1. **Vị trí file: trong thư mục chương trình, như hai chương trình tham khảo.** Cách làm: `DataRoot` mặc định = `Data`
   (thư mục con cạnh exe; đường dẫn tương đối đã được Ch17 hỗ trợ) → `<exe>\Data\{machine.json, products\<mã>\
   {product.json, jobs\*.vpp, golden, samples}, users.json, ...}`. Thư mục con riêng chứ không rải cạnh exe để
   `release.json` không băm dữ liệu (ảnh có thể hàng GB) và chép một bản phát hành mới không đè dữ liệu. Ảnh và kết quả
   có thể đặt ổ khác (`ImagesRoot`, `ResultsRoot`) như `E:\Image`/`D:\Image` của hai chương trình. Đổi phiên bản:
   chép `Data\` sang thư mục bản mới (hoặc `--backup` / `--restore`). Không cài vào Program Files (không ghi được).
2. Thanh nút theo mockup v2.2 + form **Sản phẩm** và **Camera & đèn**; các nút khác hiện nhưng báo "đang xây".
3. Làm cả bốn nhóm: bố cục khung + lệnh chọn khung; nhiều camera trong một lệnh; brightness/contrast + chờ đèn;
   lưu ảnh có đồ hoạ + mẫu tên + thư mục ổ khác + bộ đếm giữ lại + FullScreen + ký tự kết thúc theo kênh.

### Kết quả (Session 23)

- Làm đủ ba điểm đã chốt; chi tiết trong CHANGELOG Session 23. Sửa bảng A: `terminator` theo kênh đã có từ Ch21.
- **Tác giả bổ sung sau khi xem bản chạy:** chương trình + `.vpp` + cấu hình đi cùng nhau trong thư mục chương trình (tháo
  ổ sang máy đã ghost Win là chạy); ảnh và log đặt được ra ổ khác. → thêm `LogsRoot`; ổ ngoài không có thì ghi tạm vào
  `Data` + báo vàng thay vì sập. Đánh đổi: ghi tạm làm đầy ổ chương trình nếu không ai đọc cảnh báo — chấp nhận, vì mất
  ảnh NG hoặc không khởi động được còn tệ hơn; `StorageJanitor` vẫn dọn theo hạn giữ.
- **Lỗi thật lúc tác giả chạy:** VisionPro khởi động lạnh 28 s > hạn 20 s → bước then chốt hỏng → bỏ qua cả bước tài khoản →
  không đăng nhập được để chẩn đoán. Phương án: (a) chỉ tăng hạn — vẫn kẹt nếu VisionPro hỏng thật; (b) cho bước tài khoản
  chạy bất kể bước trước — phải thêm cờ vào `StartupSequence`; (c) **chọn:** đưa bước tài khoản lên trước VisionPro (nó
  chỉ cần thư mục dữ liệu) + tăng hạn lên 60 s. Đơn giản nhất, không đổi `StartupSequence`.
- Nền xám của Session 22 chỉ đúng khi chưa có ảnh: `CogRecordsDisplay` đặt lại nền mỗi lần đổi record (spike đo).
- Còn lại: sách Ch17/Ch23/Ch24 vẫn mô tả `%ProgramData%\MeoVision` (18 chỗ) — chờ tác giả quyết sửa sách; nâng cấp từ bản
  cũ phải chép dữ liệu ProgramData sang `<exe>\Data` (hoặc đặt `DataRoot` = `%ProgramData%\MeoVision`).

## [27] 2026-10-30 — Ảnh/log sang ổ D có màn cấu hình, hai lớp cấu hình an toàn khi cập nhật, sửa lỗi Thiết lập lệnh không mở (ĐÃ LÀM — Session 24)

### Bối cảnh

Tác giả: để log, ảnh sang ổ D, có chỗ cấu hình; chạy app thấy nhiều chỗ lỗi đường dẫn và không còn hiện khu vực chỉnh
sửa tool; file cấu hình theo máy và theo sản phẩm hơi nhiều, dễ lỗi khi cập nhật phần mềm mà có sửa chút thông số; đọc
các dự án tham khảo (cục bộ + GitHub) để làm chức năng tương tự; ghi mọi điều hữu ích chưa kịp làm ra một file.

**Lỗi đo được (log + crash file):** ba lần `IOException: Access to the path '...\Data\drafts\P-1024\jobs' is denied`
trong `ProductStore.OpenDraft` → form Thiết lập lệnh (khu vực chỉnh tool) không mở. Nguyên nhân: từ [26] thư mục `Data`
nằm cạnh exe, mà exe chạy từ thư mục OneDrive; OneDrive giữ thư mục `jobs` cũ (thuộc tính `ReadOnly, ReparsePoint`).
Thử thật: xoá thường và xoá sau khi bỏ thuộc tính đều thất bại ("Access denied", "The directory is not empty"). Khi dữ
liệu còn ở `%ProgramData%` lỗi này không có. Không thấy lỗi đường dẫn nào khác trong log.

**Suýt mất dữ liệu (phát hiện khi làm):** (1) ổ D của máy viết sách 20 GB, ngưỡng dọn ảnh mặc định 30 GB → mỗi lần dọn sẽ
xoá hết ảnh các ngày trước; (2) `tools/publish.ps1` đóng gói lại cùng ngày xoá cả thư mục bản phát hành — từ [26] trong đó
có `Data` đã nâng cấp.

### Dự án tham khảo (chi tiết + backlog: `docs/BACKLOG_TU_DU_AN_THAM_KHAO.md`)

RefVis-D…K mới (bảng bí danh ở thư mục private của dự án sách). Ba bài học quyết định thiết kế:
- RefVis-D bản 3.7 phải thêm bước **nạp thêm tham số từ tài nguyên của chương trình** để "sửa ở văn phòng xong đồng bộ với
  hiện trường" — tức là cập nhật phần mềm KHÔNG được đè thông số đã chỉnh tại máy, nhưng phải mang được mục mới.
- RefVis-E, RefVis-G, RefVis-H: thư mục ảnh/dữ liệu/log cấu hình được (RefVis-G có màn Cài đặt hệ thống với nút chọn thư
  mục); RefVis-F thì ghi cứng ổ D trong code — máy không có ổ D là hỏng.
- RefVis-I đọc file cấu hình lỗi thì ghi đè bằng mặc định — mất cấu hình máy. RefVis-D, RefVis-H giữ `.bak` mỗi lần lưu.

### Phương án cho "file cấu hình nhiều, dễ lỗi khi cập nhật"

| Phương án | Đánh đổi |
|---|---|
| (a) **Hai lớp:** `appsettings.json` cạnh exe = mặc định của bản phát hành (không ai sửa); `Data\settings.json` = chỉ mục trạm đổi khác mặc định | Cài bản mới không mất gì; bản mới thêm mục thì trạm tự có mặc định của mục đó; số file người sửa không tăng. Thêm một bước ghép khi đọc |
| (b) Gộp mọi thứ của máy (settings + machine.json + ngôn ngữ + sản phẩm đang chạy) vào một file trạm | Ít file nhất, giống `main.ini` của RefVis-C. Đổi lớn: Ch17–Ch19 của sách, mọi test cấu hình máy; file to, nhiều người sửa chung |
| (c) Giữ nguyên, chỉ thêm bước "gộp khi cài" như RefVis-D | Phải viết bước gộp cho từng loại file; quên một lần là mất thông số |

**Chọn (a)** — giải đúng rủi ro cập nhật mà không đảo cấu trúc sách. Gộp `ui-language.txt` vào `settings.json` và màn sửa
`machine.json` để vào backlog (C1, C7).

### Đã làm

- `App/Hosting/StationSettings.cs`: đọc `appsettings.json` → ghép `Data\settings.json` (mảng thay hẳn, null bỏ qua, mục lạ
  bỏ qua, `DataRoot` trong file trạm bị bỏ vì file nằm trong nó). File trạm hỏng → mặc định + bước "Thư mục dữ liệu" vàng,
  **không ghi đè**. `Save` chỉ ghi mục khác mặc định, giữ `settings.json.bak`, ghi qua file tạm, trả danh sách đổi
  (cũ → mới) để ghi nhật ký `settings-changed`. `Program.cs` dùng nó thay `ConfigurationBuilder`. Sao lưu trạm có thêm
  `settings.json`.
- Màn **Cài đặt → Lưu trữ và lưu ảnh** (`UI/Settings/StorageSettingsForm.cs`, quyền mới `StationSettings` = Quản trị; ai
  cũng xem): thư mục ảnh/kết quả/log + nút Chọn + dung lượng trống từng ổ, lưu NG / ảnh đồ hoạ / OK mỗi N, số ngày giữ ảnh
  và kết quả, ngưỡng dọn ổ ảnh, ngưỡng cảnh báo ổ Data, "Về mặc định". Kiểm ghi được thật từng thư mục; chặn ngưỡng dọn
  ≥ 80% dung lượng ổ ảnh; hỏi lại khi ổ đang dưới ngưỡng.
- `ProductStore.OpenDraft`: bản nháp cũ xoá không được → thư mục `drafts\<mã>-<thời điểm>` mới + log cảnh báo;
  `SetupForm` bắt lỗi chép bản nháp và báo, không văng.
- `DataFolderCheck`: thư mục Data trong OneDrive (`%OneDrive%`, `%OneDriveConsumer%`, `%OneDriveCommercial%`) → vàng.
- `tools/publish.ps1`: chuyển `Data` sang một bên khi đóng gói lại rồi trả về; không chép `Data` của `bin\Release`.
- Máy này: cài `D:\MeoVision\2.0.261030\` (chương trình + Data, ngoài OneDrive); ảnh/kết quả/log
  `D:\MeoVisionData\{Images,Results,Logs}`; `ImageMinFreeGb` 3, `MinFreeDiskGb` 5. Bản Debug: `D:\MeoVisionDev\...`.
  `--check` "SẴN SÀNG", log ra `D:\MeoVisionData\Logs`. Chạy thật (UIA): Thiết lập lệnh mở, VisionPro editor mở, màn Lưu
  trữ đọc đúng `settings.json`. File trạm sai cú pháp (gõ tay bằng heredoc) → chạy bằng mặc định + báo vàng, file giữ nguyên.
- Test 160 → **167**.

### Hệ quả

- Người cài sửa thông số trạm bằng màn hình hoặc `Data\settings.json`; KHÔNG sửa `appsettings.json` của bản phát hành.
- Sách Ch17 §17.3 (Code 17.4 `appsettings.json`) và Ch24 (`publish.ps1` được trích) cần cập nhật — backlog B1.
- Nền ảnh trong VisionPro editor vẫn xanh đậm — backlog T1.

## [28] 2026-10-31 — Phân tích khối chức năng các chương trình tham khảo, đánh giá theo góc máy công nghiệp (ĐỀ XUẤT — chờ tác giả chọn)

### Bối cảnh

Tác giả: phân tích các khối chức năng của dự án tham khảo để hoàn thiện MeoVision; chức năng nhiều chương trình làm giống
nhau mà Claude thấy không tốt thì đánh giá lại theo khía cạnh máy công nghiệp xem đã hợp lý chưa, đưa ra phương án hợp lý.

### Làm gì

Đọc lại hai review cũ (RefVis-A/C) + mã RefVis-D/E/G/H theo khối (trình tự, chờ, lỗi, giao tiếp, bypass); đếm mẫu bằng
script trên toàn bộ `.cs`. Chi tiết: `docs/PHAN_TICH_KHOI_CHUC_NANG.md` (bảng 22 khối × 6 chương trình + MeoVision;
12 cách làm phổ biến với phán xét; 10 đề xuất D1–D10; 6 quy tắc).

### Kết luận chính

- **Đúng trên máy thật dù trông "xấu":** thăm dò PLC theo chu kỳ; tắt timeout trong tool (có thời hạn ở tầng lệnh);
  log-rồi-chạy-tiếp cho lỗi phụ; file cấu hình sửa bằng Notepad; singleton cho phần mềm một máy; dừng chờ người khi máy
  có chuyển động.
- **Sai thật:** chờ không có hạn (E gửi lại lệnh xi lanh mãi mãi, H chờ chụp/kiểm không hạn); chờ trên luồng giao diện
  (E); tắt kiểm luồng giao diện (E, F, G); lỗi thành OK hoặc im lặng với máy (A script, G chụp hỏng không trả gì, D tắt
  vision trả OK); hộp thoại modal trong chu trình (E MES); `Thread.Abort` (H); cửa sổ chống nhiễu làm cơ chế duy nhất (G).
- **Cần nhưng phải làm khác:** bỏ qua vision — mọi máy thật đều có, các bản tham khảo làm im lặng → D1 chế độ BYPASS có
  tên, có hạn, có người chịu trách nhiệm.
- MeoVision đã đúng ở hầu hết điểm (ba trạng thái + lý do, bắt tay Trigger/Ack/Complete, Heartbeat, thời hạn mọi chờ,
  lưu ảnh nền, DI + test). Thiếu: bypass (D1), màn Giao tiếp (D2), dải cảnh báo không chặn (D3), gợi ý xử lý lỗi (D4),
  form Hiệu chuẩn (D5), word bước hiện tại (D6), ca (D8), nguồn ảnh thủ công cả lệnh (D9), MES (D10).

### Chờ tác giả chọn

Thứ tự đề xuất: D1 → D3 + D4 → D2 → D5.

### Tác giả chốt (2026-10-31)

Làm cả D1, D3+D4, D2, D5. Bypass: **hết hạn sau N giờ** (Kỹ sư đặt khi bật, mặc định 8 giờ ≈ một ca) **và mất khi
khởi động lại** MeoVision.

### Kết quả (Session 25, 2026-10-31)

- **D1 bỏ qua có kiểm soát:** `App/Commands/BypassService.cs` (chỉ trong bộ nhớ; lý do ≥ 5 ký tự; 0,5–12 giờ; tự hết
  hạn → sự kiện + nhật ký `bypass-expired` dưới tên `system`). `CommandStatus.Bypassed`; bộ máy chạy lệnh trả ngay, không
  đụng đèn/camera/job. TCP: `BYP,BYPASS`. PLC: word trạng thái `4`, bit mới `Bypass` = 7 (`Bit.Count` 7 → 8 — **bảng
  địa chỉ PLC đổi**), `ResultsValid` và `Error` đều tắt. Bộ đếm OK/NG/ERR không đếm BYP; CSV ghi `BYPASSED`, Lịch sử lọc
  được. Form **Cài đặt → Bỏ qua kiểm** (Kỹ sư, xác nhận trước khi bật), nhật ký `bypass-on`/`bypass-off`.
- **D3 dải cảnh báo:** `App/Alerts/AlertCenter.cs` + `AlertFeeder.cs` (thiết bị Faulted, kênh PLC mất, bỏ qua đang bật,
  lỗi lưu ảnh/kết quả — tự xoá sau 10 phút yên, bước khởi động hỏng, cảnh báo của 3 bước chọn lọc). `UI/Display/AlertBar.cs`
  dưới thanh nút: đỏ/vàng, ẩn khi bình thường, bấm xem danh sách. Lỗi tìm ra khi chạy thật: dải ẩn chưa có handle nên
  không bao giờ hiện — chuyển sự kiện qua form chứa nó.
- **D4 cách xử lý:** khoá `<khoá>.hint` (25 thông điệp: thiết bị, camera, lệnh, khởi động, bản phát hành, cảnh báo) — hiện
  ở cột "Cách xử lý" của danh sách cảnh báo và tooltip từng dòng Nhật ký.
- **D2 màn Giao tiếp:** `App/Comm/CommLog.cs` (500 bản tin, mọi kênh), `CommandService.TestAsync` (chỉ khi OFFLINE),
  `PlcChannel.LastBits`; `UI/Comm/CommForm.cs` (kênh, bản tin vào/ra + ms, bit bắt tay PLC trực tiếp, gửi thử). Nút
  Giao tiếp mở form (ai cũng xem).
- **D5 Hiệu chuẩn:** `CalibrationService.List/Points/History/RestoreFromHistory/CancelSession` (+ khoá `_sessions` cho
  luồng giao diện); `UI/Calibration/CalibrationForm.cs` (danh sách, RMS lần tính cuối so ngưỡng, sai số từng điểm, bản cũ,
  dùng lại bản cũ — sản phẩm không nạp được thì trả lại bản đang dùng).
- Chạy thật (bản cài `D:\MeoVision\2.0.261031`, nâng cấp từ 261030 bằng `--backup`/`--restore` — 21 file, có
  `settings.json`): bật bỏ qua T1 qua form → TCP trả `T1,SN-BYP-1,BYP,BYPASS,`; dải đỏ "MainLight is not connected";
  danh sách cảnh báo có cách xử lý; Giao tiếp ghi `T1,P-1024,SN-1 → ERR,LIGHT` 39 ms; Hiệu chuẩn liệt kê TopCal.
  Sửa thêm khi chạy ở 125%: tên sản phẩm bị đẩy vào nút tràn (nút tối thiểu 96 → 84 px, chữ sản phẩm 13 → 11,5 pt);
  hàng nút cuối màn Giao tiếp bị cắt (panel tự cao); danh sách kênh nhấp nháy (cập nhật tại chỗ).
- Test 167 → **177**.
- Còn: D6 word bước hiện tại, D7 `errorValue`, D8 ca, D9 nguồn ảnh thủ công, D10 MES; lưu ảnh từng điểm hiệu chuẩn.

## [29] 2026-10-31 — Không gửi lý do cho máy; phân biệt lệnh kiểm tra và lệnh dẫn hướng (ĐÃ LÀM — Session 26)

### Bối cảnh

Tác giả: gửi kết quả NG kèm lý do là không cần — chỉ cần gửi kết quả, lý do ghi log và file dữ liệu; nhiều máy dùng vision
để lấy tham số điều khiển chuyển động (X, Y, góc), máy AOI là dạng máy test khác hẳn. Bỏ qua kiểm chủ yếu ở máy test (sản
phẩm phải qua trạm), không ở máy có tham số điều khiển — cần phân biệt.

### Phương án đã hỏi và tác giả chọn

| Câu hỏi | Phương án | Chọn |
|---|---|---|
| Bỏ lý do khỏi bản tin | (a) giữ chỗ trường, để trống · (b) bỏ hẳn trường (đổi định dạng, PLC + sách sửa) · (c) tuỳ chọn theo kênh | **(a)** — chương trình PLC đọc theo vị trí không phải sửa |
| ERR | chỉ gửi ERR · vẫn gửi mã lỗi ngắn | **chỉ ERR** — máy chỉ cần biết "không có kết quả dùng được" |
| Phân biệt loại | theo từng lệnh (`kind` trong product.json) · theo cả máy (machine.json) | **theo lệnh** — một sản phẩm có thể vừa dẫn hướng vừa kiểm cuối; RefVis-A cũng đánh dấu loại theo từng chức năng |

### Đã làm

- `MessageProtocol.WithoutReason`; `CommandService` tính lý do như cũ nhưng gửi máy với trường lý do TRỐNG (TCP); PLC:
  word `Reason` luôn 0. Lý do vào log ứng dụng (`reason ...`), màn Giao tiếp (cột "Lý do (không gửi)"), và CSV — cột
  `error` nay có cả lý do NG: `LIMIT:<output>;...` hoặc `REJECT`.
- `CommandConfig.Kind` = `Inspect` (mặc định) | `Guide` (`CommandKind`); bộ nạp từ chối giá trị khác (`product.cmd.badKind`).
  Bộ máy chạy lệnh KHÔNG bao giờ trả BYP cho lệnh `Guide` (chặn cả khi bỏ qua bật trước khi đổi sang sản phẩm có cùng mã
  là lệnh dẫn hướng); form Bỏ qua kiểm hiện "Dẫn hướng — không bỏ qua được", nút bật bị khoá; form Sản phẩm có cột Loại.
- Test 177 → 179 (cập nhật 11 khẳng định cũ về lý do trong bản tin → kiểm lý do trong nhật ký bản tin). Chạy thật bản cài
  ổ D: `T1,SN-A1,ERR,,` · màn Giao tiếp cột lý do LIGHT / PRODUCT / UNKNOWN.

### Hệ quả

- Sách Ch21 (bản tin có lý do LIMIT/CAMERA...) và Ch22–23 cần sửa — backlog B3.
- Lệnh dẫn hướng NG (không tìm thấy / ngoài giới hạn bù) vẫn trả NG/ERR — máy không di chuyển theo giá trị đó.

## [30] 2026-11-01 — Treo "Not Responding" khi để yên lâu: COM tháo DLL VisionPro giữa chừng (ĐÃ LÀM — Session 27)

### Bối cảnh

Tác giả gửi ảnh: bản cài `D:\MeoVision\2.0.261031` treo ("Thiết lập lệnh … Not Responding") ngay sau khi tự đăng xuất do
không thao tác. Tiến trình treo thật, không tự hồi.

### Cách tìm nguyên nhân (chỉ đọc, không làm phiền tiến trình)

1. Chồng gọi managed (ClrMD gắn sống): luồng giao diện chỉ thấy đang ở vòng thông điệp — vô ích, phần treo ở mã native.
2. Chồng gọi native (công cụ StackWalk64 tự viết, dừng luồng vài ms để đọc CONTEXT):
   - một luồng nền của COM đang `CoFreeUnusedLibrariesEx` → `FreeLibrary(cogxImagingDevice.dll)` → `DllMain` (DETACH) →
     `SendMessageW` tới một cửa sổ của luồng giao diện — **đang giữ loader lock**;
   - luồng giao diện đang trong CLR chờ chính loader lock đó → không bơm thông điệp → hai bên chờ nhau mãi.
3. Vì sao "khi để yên lâu": COM chỉ tháo DLL in-proc sau khoảng 10 phút không dùng; trùng với tự đăng xuất nên trông như
   do đăng xuất.
4. Spike kiểm chứng (exe riêng): nạp tầng chụp ảnh VisionPro trên luồng MTA (như `Task.Run` mở thiết bị/watchdog) rồi gọi
   `CoFreeUnusedLibrariesEx` → **6/7 `cogx*.dll` bị tháo**; ghim trước → **cả 7 còn nguyên**.

### Phương án

| Phương án | Đánh đổi | Chọn |
|---|---|---|
| A. Ghim (`GetModuleHandleEx(GET_MODULE_HANDLE_EX_FLAG_PIN)`) mọi `cogx*.dll` sau khởi động + quét lại mỗi 30 s | DLL không bao giờ tháo (đằng nào cũng dùng suốt ca); không đụng VisionPro; vài dòng | **✔** |
| B. Giữ một đối tượng VisionPro sống suốt (CogFrameGrabbers tĩnh) | phụ thuộc DLL nào được đối tượng đó giữ; dễ sót DLL nạp sau | ✘ |
| C. Chỉ dùng VisionPro trên luồng STA giao diện | đổi kiến trúc làn camera/watchdog, chậm giao diện | ✘ |
| D. Watchdog tự khởi động lại khi treo | chữa triệu chứng, mất lệnh đang chạy | ✘ (có thể thêm sau như lớp bảo vệ chung) |

### Đã làm

- `src/MeoVision.Vision/Runtime/NativeModulePin.cs` — `PinLoadedCognexModules()` trả tên module vừa ghim.
- `Program.cs` — ghim ngay trước `Application.Run` + `Timer` 30 s cho DLL nạp sau (mở thiết bị, nạp job); log
  `Pinned VisionPro native modules: ...`.
- Test `NativeModulePinTests` (179 → 180): sau khi ghim, `CoFreeUnusedLibrariesEx` không làm mất `cogx*.dll` nào.
- Phát hành `2.0.261101`.

### Hệ quả

- Bài học cho sách (Ch18/Ch24): ứng dụng giữ thư viện COM native suốt ca nên ghim DLL; "treo khi để yên" → nghĩ tới việc
  hệ thống dọn tài nguyên rảnh (COM unload, idle timer), không chỉ mã của mình. Backlog B3.

## [31] 2026-11-03 — Chạy kèm bản nháp báo sai "khác kết quả" với mọi job có Fixture (ĐÃ LÀM — Session 29)

### Bối cảnh

Chạy thật `samples/toolchains/build.cmd shots` (job mẫu của Session 28 trên cloud): ToolShots nạp MỘT ảnh rồi cho cả 3 job
chạy lần lượt — job 2 và 3 lỗi ở tool Fixture: *"This fixture tool has just modified a coordinate space that it did not
create"*. Chạy kèm của MeoVision (`ShadowRunner` → `DraftSession.Run`) làm đúng như vậy: job nháp (đối tượng nạp riêng)
chạy trên ảnh mà job đang chạy vừa gắn hệ `Fixture` vào; vòng ảnh gần nhất cũng thế. Job mẫu P-1024 không có Fixture nên
test cũ không bắt được; job thật hầu như luôn có Fixture → chạy kèm sẽ báo "khác" ở MỌI lệnh.

### Đo thật (spike `FixSpike2`, mỗi trường hợp job + ảnh nạp mới, lặp 2 vòng cùng kết quả)

| Ảnh đưa cho job thứ hai (B) sau khi job A đã chạy | Cây hệ toạ độ | B |
|---|---|---|
| Cùng ảnh | dùng chung | Error |
| `img.Copy(CopyPixels)` sau A | dùng chung | Error |
| `img.Copy(CopyPixels)` lấy TRƯỚC khi A chạy | **vẫn dùng chung** — bản sao thấy `@\Fixture` A thêm sau | Error |
| `img.CopyBase(CopyPixels)` | dùng chung | Error |
| `new CogImage8Grey(img)` | riêng, giữ `@\calib` có trước | **Accept**, output trùng A |

Cùng một đối tượng job chạy lại trên cùng ảnh thì không lỗi (tool nhận ra hệ do chính nó tạo).

### Phương án

| Phương án | Đánh đổi | Chọn |
|---|---|---|
| A. Job nháp chạy trên bản sao bằng constructor sao chép (`CameraImage.CopyForAnotherJob`) | chép điểm ảnh mỗi lần chạy nháp (vài ms với 5 MP); đúng cho mọi job, giữ hiệu chuẩn | **✔** |
| B. Đặt `FixturedSpaceNameDuplicateHandling = Compatibility` cho Fixture của job nháp | sửa job của người dùng; job nháp khác job sẽ áp dụng | ✘ |
| C. Đổi `FixturedSpaceName` của job nháp thành tên riêng | như B, và script tham chiếu tên hệ sẽ sai | ✘ |
| D. Giữ một bản ảnh "sạch" trước khi job thật chạy | phải biết trước lệnh nào cần chạy kèm; `Copy()` không tách cây nên vẫn phải dùng constructor — quay về A | ✘ |

### Đã làm

- `Core/Devices/CameraImage.CopyForAnotherJob()` (ảo, mặc định null = ảnh không mang hệ toạ độ); `CogCameraImage` cài bằng
  constructor sao chép cho `CogImage8Grey`/`CogImage16Grey`/`CogImage24PlanarColor`.
- `DraftSession.Run` chạy job nháp trên bản sao; khung kết quả sở hữu bản sao (cùng điểm ảnh, record của bản nháp).
- Test `FixtureSharedImageTests` (180 → 182) dùng job C1 thật: đối tượng thứ hai lỗi trên cùng ảnh, chạy được và ra cùng
  output trên bản sao; bản sao giữ hệ có trước, không thấy hệ thêm sau.
- `samples/toolchains`: ToolShots cho mỗi job một ảnh riêng; `CogCaliper` không có `EdgePairWidth` (dùng
  `Edge0Position`/`Edge1Position`); `build.cmd` gọi exe bằng đường dẫn đầy đủ (máy có `NoDefaultCurrentDirectoryInExePath`).
  `build.cmd` → ALL MATCH 8 ảnh × 3 job; 16 ảnh chụp editor thật chèn vào `VISIONPRO_TOOLS.md`.

### Hệ quả

- Sách Ch22 (chạy kèm) + Ch7 (Fixture) cần thêm mục lỗi này — backlog B3.
- Quy tắc chung: hai đối tượng job khác nhau không bao giờ chạy trên cùng một `ICogImage`; `Copy()` của VisionPro không
  phải bản sao độc lập về hệ toạ độ.
