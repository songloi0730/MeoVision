---
name: meovision-engine-patterns
description: Dùng khi thêm/sửa code trong project MeoVision.Engine — project net48 DUY NHẤT được phép reference Cognex.VisionPro.*. Kích hoạt khi tác giả nói "thêm tool vào Engine", "gọi API VisionPro thật", hoặc chỉnh sửa bất kỳ file nào trong src/MeoVision.Engine/.
---

# MeoVision.Engine patterns

`MeoVision.Engine` là project **net48 duy nhất** chạm Cognex thật. Mọi API `Cog*` dùng trong đây
phải qua đúng quy trình kiểm chứng đã áp dụng suốt sách (reflection trên DLL thật trước, XML doc
bổ sung sau — xem CLAUDE.md của dự án sách nếu cần nhắc lại chi tiết lệnh PowerShell).

## Quy tắc bắt buộc
1. **Timeout mọi lệnh gọi SDK**: `using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct); cts.CancelAfter(timeoutMs);`
2. **Không bao giờ để kiểu `Cognex.VisionPro.*` lọt ra khỏi project này** — đây chính là lý do
   project tồn tại (xem `docs/design-notes/DESIGN_LOG.md` mục [2]). Nếu thấy
   code nào cố `using Cognex.VisionPro` ở project khác ngoài Engine → dừng lại, đó là vi phạm
   kiến trúc (ranh giới trong-process, enforce bằng review/skill, không phải bằng process OS).
3. **Map kết quả Cognex → `VisionResult` trung lập** ngay tại boundary của Engine — không để kiểu
   `CogPMAlignResult`/`CogBlobResult`... lọt ra ngoài qua return type public của `IVisionEngine`.
4. **Mọi thay đổi ở đây kèm kiểm tra `reference/Outline_To_Book_Mapping.md`** — API mới có khớp
   những gì sách đã dạy ở chương tương ứng không.
