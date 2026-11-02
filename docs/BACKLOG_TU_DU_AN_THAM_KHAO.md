# Backlog — học từ các dự án vision tham khảo

> Ghi mọi điều hữu ích đọc được trong mã nguồn/cấu hình của các chương trình thật, kèm **vì sao họ làm vậy**,
> để làm dần. Tên thật của dự án KHÔNG ghi ở đây — chỉ bí danh `RefVis-*`; bảng đối chiếu nằm trong
> `docs/private/RefVis_Alias_Map.md` của dự án sách (không công khai).
>
> Cách dùng: mỗi việc có mã (C1, S2...), mức ưu tiên **P1** (nên làm sớm) / **P2** (đáng làm) / **P3** (khi rảnh),
> trạng thái ☐ chưa / ◐ đang / ☑ xong. Làm xong thì đổi trạng thái và ghi Session + DESIGN_LOG.
>
> Lập: 2026-10-30 (MeoVision Session 24, DESIGN_LOG [27]).

> **2026-10-31:** phân tích theo khối + đánh giá cách làm phổ biến theo góc máy công nghiệp → `docs/PHAN_TICH_KHOI_CHUC_NANG.md`,
> đề xuất D1–D10 (bypass có kiểm soát, màn Giao tiếp, dải cảnh báo, gợi ý xử lý lỗi, form Hiệu chuẩn...).

## 1. Đã đọc những gì

| Bí danh | Loại | Đã đọc | Ghi chú |
|---|---|---|---|
| RefVis-A | WinForms + VisionPro, máy lắp ráp | Session 22–26 + 2026-10-30 | `PublicSystem.ini` + mỗi sản phẩm một thư mục `AppConfig.ini` + `ToolBlock\*.vpp` |
| RefVis-C | Khung vision WinForms + VisionPro, định vị vít 2 camera | Session 25–26 + 2026-10-30 | `main.ini` (máy) + `config.ini` (dự án), ghép lệnh, lưu ảnh gốc/ảnh cửa sổ |
| RefVis-D | Khung máy tự động có mô-đun vision (Halcon), WinForms | 2026-10-30 | file ini của khung, file tham số hệ thống XML + bản "lần trước" + bản "mặc định", thư mục cấu hình vision theo sản phẩm (`param.ini`), lịch sử phiên bản trong mã |
| RefVis-E | AOI 3 camera, VisionPro, WinForms | 2026-10-30 | Một `Configuration.ini` (ảnh/log/dữ liệu tách thư mục, số ngày giữ riêng), mỗi camera một `.vpp` |
| RefVis-F | Máy đo/kiểm, VisionPro, WinForms (2018) | 2026-10-30 | Thư mục tham số **cố định trên ổ D**, nhiều file ini/xml/csv theo chức năng |
| RefVis-G | Kiểm ngoại quan, VisionPro + nhiều loại camera, WinForms | 2026-10-30 | `Setting.ini` + màn Cài đặt hệ thống (chọn thư mục ảnh/dữ liệu), mỗi model một thư mục: file thông số camera + tool theo camera |
| RefVis-H | Kiểm lắp ráp, VisionPro + PLC + SQL Server | 2026-10-30 | một file XML trong thư mục `Config`, mặc định trong code, lưu kèm `.bak`, ngưỡng đĩa theo % |
| RefVis-I | Kiểm bán dẫn, WPF | 2026-10-30 | một file JSON — **phản mẫu**: đọc lỗi thì ghi đè bằng mặc định |
| RefVis-J | Khung kiểm tra WPF (portfolio) | 2026-10-30 (lướt) | Mỗi lệnh 3 file tool: căn chỉnh / hiệu chuẩn / chạy; cờ lưu ảnh gốc + ảnh phủ |
| RefVis-K | Khung WPF Halcon + VisionMaster + Hik | 2026-10-30 (lướt) | Cấu hình có số phiên bản + hàm nâng cấp theo phiên bản |

**Chưa đọc** (có tham chiếu thư viện vision trong `.csproj`, danh sách thật trong bảng bí danh): 11 dự án cục bộ và
trên GitHub — gắp đặt dán nhãn 2 camera dưới, khung Halcon, kiểm lát cắt, kiểm phím, robot kiểm tra, handler, tester,
3 khung máy khác. Xem mục 5.

## 2. Họ tổ chức cấu hình thế nào — và vì sao

| Cách | Ai làm | Vì sao họ làm vậy | Cái giá |
|---|---|---|---|
| **Một file INI cho cả máy** cạnh exe | RefVis-E, RefVis-G, RefVis-A (`PublicSystem.ini`), RefVis-C (`main.ini`) | Kỹ thuật viên mở Notepad sửa được; INI **đọc kèm giá trị mặc định**  nên bản mới thêm khoá thì file cũ vẫn chạy | Chép bản mới đè cả thư mục là mất; không kiểm kiểu; mật khẩu nằm chữ thường trong file (RefVis-E) |
| **Mỗi sản phẩm một thư mục** (ini + tool `.vpp` + ảnh mẫu) | RefVis-A, RefVis-C, RefVis-D, RefVis-G | Đổi sản phẩm = đổi thư mục; chép sản phẩm sang máy khác = chép thư mục | Thư mục sản phẩm thường chứa cả thông số máy (serial camera, cổng) → chép sang máy khác vẫn phải sửa tay |
| **Tham số trên ổ D cố định** | RefVis-F | Cài lại Windows/chương trình trên C không mất thông số | Đường dẫn cứng trong code: máy không có ổ D là hỏng, không đổi được |
| **Bảng tham số có min/max/quyền + nút "lần trước"/"mặc định"** | RefVis-D (file tham số + bản "lần trước" + bản "mặc định") | Người vận hành sửa sai có đường lui ngay trên màn hình; quyền theo từng tham số | Ba file song song dễ lẫn bản nào đang chạy |
| **Nạp thêm tham số từ tài nguyên trong chương trình** | RefVis-D bản 3.7 | Ghi chú phiên bản của họ: sửa tham số ở văn phòng xong phải "đồng bộ với hiện trường" — bản mới mang định nghĩa tham số mới, **gộp** vào file hiện trường mà không đè giá trị đã chỉnh | Phải tự viết bước gộp cho từng loại dữ liệu (tham số, điểm, cấu hình dữ liệu) |
| **XML/JSON tuần tự hoá class, mặc định trong code** | RefVis-H, RefVis-I | Thêm thuộc tính mới = thêm field có giá trị mặc định; file cũ thiếu field vẫn đọc được | RefVis-I đọc lỗi thì GHI ĐÈ bằng mặc định → mất cấu hình máy |
| **Cấu hình có số phiên bản + hàm nâng cấp** | RefVis-K | Đổi cấu trúc file mà không bắt người dùng sửa tay | Phải viết và test từng bước nâng cấp |

**MeoVision chọn (DESIGN_LOG [27]):** `appsettings.json` cạnh exe = mặc định của bản phát hành, không ai sửa;
`Data\settings.json` = chỉ những gì trạm này đổi (giống ý "gộp, không đè" của RefVis-D 3.7 nhưng làm sẵn ở lớp đọc);
file trạm hỏng thì chạy bằng mặc định + báo vàng + **không ghi đè** (tránh RefVis-I); mỗi lần lưu giữ `.bak`
(RefVis-D, RefVis-H). Sản phẩm vẫn là thư mục tự chứa (`product.json` + `jobs\*.vpp`), chỉ tham chiếu vai trò thiết bị.

### Các file của MeoVision — ai sửa

| File | Ai sửa | Ghi chú |
|---|---|---|
| `appsettings.json` (cạnh exe) | Không ai — đi theo bản phát hành | Cài bản mới thay luôn |
| `Data\settings.json` | Màn **Cài đặt → Lưu trữ và lưu ảnh** (Quản trị) | Chỉ chứa mục khác mặc định |
| `Data\machine.json` | Kỹ sư cài máy (tay, chưa có màn hình — xem C7) | Camera, đèn, kênh của máy này |
| `Data\products\<mã>\product.json` + `jobs\*.vpp` | Thiết lập lệnh (bản nháp → Áp dụng) | Có sao lưu theo phiên bản |
| `users.json`, `counters.json`, `active-product.txt`, `ui-language.txt`, `audit\` | Chương trình tự quản | Không sửa tay |

## 3. Việc nên làm

### Cấu hình và cập nhật phần mềm

| Mã | Ưu tiên | Việc | Vì sao / học từ | Trạng thái |
|---|---|---|---|---|
| C0 | P1 | Hai lớp cấu hình `appsettings.json` + `Data\settings.json`, màn Lưu trữ | Cập nhật phần mềm không mất thông số đã chỉnh (yêu cầu tác giả 2026-10-30) | ☑ Session 24 |
| C1 | P1 | Gộp `ui-language.txt` vào `settings.json` (`UiLanguage`) — hiện hai chỗ cùng nói một điều | Bớt một file; một nguồn sự thật | ☐ |
| C2 | P2 | "Nhập sản phẩm từ văn phòng": gộp `product.json` mới vào bản hiện trường, giữ giá trị tunables/giới hạn đã chỉnh, báo khác biệt | RefVis-D 3.7 (nạp thêm tham số, điểm teach, cấu hình dữ liệu từ tài nguyên của chương trình) | ☐ |
| C3 | P2 | `schemaVersion` của `product.json`/`machine.json` tăng → bước nâng cấp tự động + `.bak`, thay vì từ chối | RefVis-K: hàm nâng cấp theo số phiên bản | ☐ |
| C4 | P2 | Mỗi thông số trạm có min/max/quyền riêng, hiện trong bảng (như tunables của lệnh) | RefVis-D 3.8: quyền theo từng tham số | ☐ |
| C5 | P2 | Nút "Khôi phục lần lưu trước" (từ `settings.json.bak`) trên màn Lưu trữ | RefVis-D Form tham số: "lần trước" + "mặc định" | ☐ |
| C6 | P2 | Bỏ `MissingMemberHandling.Error` khi **quay về bản cũ** đọc file của bản mới: báo vàng thay vì từ chối | Quay lại bản cũ là đường lui khi bản mới lỗi (Ch24) | ☐ (cần cân nhắc: lỗi gõ nhầm tên trường cũng bị lọt) |
| C7 | P2 | Màn Giao tiếp / Camera & đèn sửa `machine.json` (cổng COM, IP, serial) thay vì sửa tay | RefVis-G sửa COM/baud trên màn Cài đặt | ☐ |

### Lưu ảnh và dung lượng

| Mã | Ưu tiên | Việc | Vì sao / học từ | Trạng thái |
|---|---|---|---|---|
| S1 | P1 | Số ngày giữ **riêng** cho ảnh gốc và ảnh có đồ hoạ (`_view.jpg`) | RefVis-E: BMP 5 ngày, JPG 10 ngày — ảnh gốc to, ảnh đồ hoạ nhỏ mà đủ để QA xem | ☐ |
| S2 | P1 | Định dạng ảnh gốc: BMP hoặc PNG nén không mất (đo tốc độ ghi + dung lượng bằng spike) | 2 GB = 1003 ảnh BMP NG trong 18 phút chạy thử (2026-10-30) | ☐ |
| S3 | P2 | Chế độ lưu cho từng lệnh: không / chỉ NG / tất cả — riêng ảnh gốc và ảnh đồ hoạ | RefVis-D: hai tham số lưu ảnh gốc / ảnh cửa sổ, mỗi cái 0/1/2; RefVis-G OK/NG/NG-kèm-giao-diện | ☐ |
| S4 | P2 | Ngưỡng dọn đĩa theo **%** ổ (ngoài GB) — đúng cho mọi cỡ ổ | RefVis-H: ngưỡng % ổ đã dùng (90); lỗi suýt gặp: ngưỡng 30 GB trên ổ D 20 GB xoá hết ảnh | ☐ (màn Lưu trữ đã chặn ngưỡng ≥ 80% ổ) |
| S5 | P2 | Dọn đĩa bỏ qua thư mục vừa tạo vài phút | RefVis-D 3.9: thư mục rỗng vừa tạo bị xoá trước khi kịp ghi | ☐ (MeoVision đã không đụng ngày hôm nay — kiểm lại lúc qua nửa đêm) |
| S6 | P2 | Số ngày giữ log trên màn Lưu trữ (`LogRetainedFileCount`) | RefVis-E: số ngày giữ log cấu hình được | ☐ |
| S7 | P3 | Màn duyệt ảnh theo cây ngày/lệnh/OK-NG | RefVis-D: màn ảnh dạng cây thư mục | ☐ (Lịch sử đã mở được ảnh từng dòng) |

### Sản xuất

| Mã | Ưu tiên | Việc | Vì sao / học từ | Trạng thái |
|---|---|---|---|---|
| P1 | P2 | Bộ đếm theo **ca** (giờ đổi ca ngày/đêm), tuỳ chọn tự về 0 mỗi ngày | RefVis-E: giờ đầu ca ngày/đêm, tuỳ chọn sản lượng về 0 mỗi ngày; RefVis-H: thuộc tính ca | ☐ |
| P2 | P3 | Nhập mã công nhân khi đăng nhập ca (độ dài cố định, tự đóng) | RefVis-D 3.8 | ☐ |
| P3 | P2 | Gửi kết quả lên MES / CSDL (kênh client) | RefVis-E (mục tải lên MES), RefVis-H (CSDL SQL Server) | ☐ (đã có trong DESIGN_LOG [26] mục "đề xuất sau") |

### Thiết lập / teach

| Mã | Ưu tiên | Việc | Vì sao / học từ | Trạng thái |
|---|---|---|---|---|
| T1 | P1 | Nền vùng ảnh trong **VisionPro editor** (Thiết lập lệnh → Mở editor) vẫn xanh đậm — đặt xám như màn chính | Ý tác giả (Session 22); chụp 2026-10-30 thấy editor còn nền xanh | ☐ |
| T2 | P2 | Ảnh mẫu (master) của từng lệnh hiện cạnh ảnh đang chỉnh | RefVis-G: ảnh master theo camera trong thư mục model | ☐ (thư mục `golden` đã có) |
| T3 | P2 | Lưu/nạp thông số camera (gain, exposure, GenICam) theo sản phẩm; nạp lại job khi đổi | RefVis-G: file thông số camera theo model, nạp lại `.vpp` sau khi đổi gain; RefVis-J file cấu hình camera | ☐ |
| T4 | P3 | Tách tool căn chỉnh / hiệu chuẩn / chạy thành 3 file | RefVis-J | ☐ (MeoVision: hiệu chuẩn theo máy, job theo lệnh — có thể đủ) |

### Giao diện

| Mã | Ưu tiên | Việc | Trạng thái |
|---|---|---|---|
| U1 | P1 | Form Hiệu chuẩn, Giao tiếp (đang báo "đang xây") | ☐ |
| U2 | P2 | Màn Lưu trữ: cột nhãn hẹp lại để ô đường dẫn rộng hơn | ☐ |

### Từ phân tích khối chức năng (DESIGN_LOG [28])

| Mã | Ưu tiên | Việc | Trạng thái |
|---|---|---|---|
| D1 | P1 | Bỏ qua kiểm có kiểm soát | ☑ Session 25 |
| D2 | P1 | Màn Giao tiếp | ☑ Session 25 |
| D3 | P1 | Dải cảnh báo không chặn | ☑ Session 25 |
| D4 | P1 | Cách xử lý cho từng mã lỗi (`*.hint`) | ☑ Session 25 (25 thông điệp; thêm dần) |
| D5 | P2 | Form Hiệu chuẩn | ☑ Session 25 (chưa lưu ảnh từng điểm) |
| D6 | P2 | Word "bước hiện tại" tới PLC | ☐ |
| D7 | P3 | `errorValue` (vd 999) cho đầu ra PLC — chỉ khi máy cần | ☐ |
| D8 | P2 | Bộ đếm + nhịp theo ca | ☐ |
| D9 | P2 | Nguồn ảnh thủ công cho cả lệnh (chạy rỗng) | ☐ |
| D10 | P3 | Kênh MES/CSDL | ☐ |
| D11 | P2 | Lưu ảnh từng điểm hiệu chuẩn (RefVis-C lưu, dùng khi RMS xấu) | ☐ |
| B3 | P1 | Sách: bỏ qua kiểm (chỉ lệnh Inspect), `kind` Inspect/Guide, bản tin không còn lý do, dải cảnh báo, màn Giao tiếp/Hiệu chuẩn, bit PLC 7 (Ch19–Ch23) | ☐ |

### Môi trường và triển khai

| Mã | Ưu tiên | Việc | Vì sao | Trạng thái |
|---|---|---|---|---|
| E1 | P1 | Cảnh báo khi thư mục Data nằm trong OneDrive | Lỗi thật 2026-10-30: OneDrive khoá bản nháp → Thiết lập lệnh không mở | ☑ Session 24 |
| E2 | P1 | Mở bản nháp không được thì dùng thư mục mới, không văng lỗi | Cùng lỗi trên | ☑ Session 24 |
| E3 | P1 | `publish.ps1` đóng gói lại cùng ngày không xoá `Data` trong thư mục bản phát hành | Suýt xoá dữ liệu đã nâng cấp (2026-10-30) | ☑ Session 24 |

### Sách

| Mã | Ưu tiên | Việc | Trạng thái |
|---|---|---|---|
| B1 | P1 | Ch17 §17.3 + Ch24: hai lớp cấu hình (`settings.json`), màn Lưu trữ, cảnh báo OneDrive; Ch24 tiêm lại `publish.ps1` | ☐ |
| B2 | P2 | Chụp lại Hình 22.2, 22.3, 23.1, 23.3, 23.4 theo thanh nút mới | ☐ |

## 4. Bài học về cách các chương trình thật hỏng (để đừng lặp lại)

- Đường dẫn cứng trong code (RefVis-F ổ D cố định; RefVis-H chuỗi kết nối CSDL ghi chết theo tên máy dev).
- Đọc cấu hình lỗi → ghi đè bằng mặc định (RefVis-I).
- Mật khẩu chữ thường trong file cấu hình (RefVis-E).
- Bản sao `.vpp` "bản sao"/"thử" và thư mục sản phẩm đã xoá nằm lẫn trong thư mục chương trình (RefVis-A, RefVis-C) —
  MeoVision cảnh báo `.vpp` không lệnh nào dùng (trạm này còn `T3_blobs - Copy.vpp`).
- Lịch sử phiên bản ghi trong mã (RefVis-D) — đọc được *vì sao* từng thay đổi; MeoVision có CHANGELOG + DESIGN_LOG.

## 5. Còn phải đọc

| Việc | Ghi chú |
|---|---|
| 11 dự án có vision chưa đọc (danh sách trong bảng bí danh) | Đọc theo câu hỏi cụ thể (cấu hình, lưu ảnh, teach, MES), ghi thêm vào mục 3 |
| RefVis-D: màn cấu hình/gỡ lỗi vision (Halcon) | So với Thiết lập lệnh của MeoVision |
| RefVis-D/khung máy cùng họ: file lưới điểm theo sản phẩm | Có thể gợi ý cho lệnh nhiều vị trí |
