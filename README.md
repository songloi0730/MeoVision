# MeoVision

Phần mềm vision công nghiệp dùng **Cognex VisionPro**, viết làm ví dụ xuyên suốt cho **Phần VI**
của sách "Xử lý ảnh công nghiệp với Cognex VisionPro": một phần mềm dùng chung cho nhiều loại máy,
cấu hình riêng của máy và của sản phẩm tách khỏi chương trình, giao diện đa ngôn ngữ. Mỗi chương
của Phần VI là một khối chức năng; code trích trong sách lấy nguyên văn từ repo này.

- Trạng thái: [`PROJECT_STATUS.md`](PROJECT_STATUS.md)
- Quyết định thiết kế: [`docs/design-notes/DESIGN_LOG.md`](docs/design-notes/DESIGN_LOG.md) — Phần VI từ mục [9]
- Tool VisionPro thường dùng + job nối nhiều tool (script đầy đủ, ảnh mẫu có đáp án): [`docs/vision-tools/VISIONPRO_TOOLS.md`](docs/vision-tools/VISIONPRO_TOOLS.md)
- Bản V0–V3 cũ (có engine giả lập): thư mục `legacy/`, không thuộc solution

## Yêu cầu

- Windows x64, .NET Framework 4.8 Developer Pack, Visual Studio 2022 (MSBuild)
- **Cognex VisionPro 9.x x64 đã cài và có license** — bắt buộc cả để build lẫn chạy; đường dẫn DLL
  mặc định `C:\Program Files\Cognex\VisionPro\ReferencedAssemblies`, đổi bằng `/p:CognexVisionProDir=...`

## Cấu trúc

| Project | Vai trò |
|---|---|
| `src/MeoVision.Core` | Interface, mô hình, `Msg` — không tham chiếu gì |
| `src/MeoVision.Vision` | Mọi thứ gọi Cognex (môi trường/license, sau này: camera, job `.vpp`) |
| `src/MeoVision.App` | Vòng đời, thư mục dữ liệu, bộ dịch — không Cognex |
| `src/MeoVision.UI` | WinForms, `Program.Main` (composition root), `languages/*.json` |
| `tests/*` | xUnit + FluentAssertions; `MeoVision.Vision.Tests` chạy thật trên VisionPro |

Dữ liệu nằm ở `%ProgramData%\MeoVision` (đổi bằng `DataRoot` trong `appsettings.json`): `machine.json`
và hiệu chuẩn của MÁY, `products\<mã>` cho từng SẢN PHẨM (job `.vpp`, cấu hình, ảnh mẫu), log, ảnh,
kết quả, sao lưu.

## Build và test

```bash
"/c/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/amd64/MSBuild.exe" MeoVision.sln -restore -p:Configuration=Debug
```

```bash
"/c/Program Files/Microsoft Visual Studio/2022/Community/Common7/IDE/Extensions/TestPlatform/vstest.console.exe" tests/MeoVision.App.Tests/bin/Debug/net48/MeoVision.App.Tests.dll tests/MeoVision.Vision.Tests/bin/Debug/net48/MeoVision.Vision.Tests.dll //Platform:x64
```
