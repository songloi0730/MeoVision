# MeoVision.UI (net48 WinForms)

`FormMain` — 4 tab (Vận hành/Lịch sử/Xu hướng/Cấu hình), `PictureBox`+GDI+ overlay, Timer-driven
run loop, tab Xu hướng tự vẽ GDI+ (rolling window score/thời gian xử lý, không thư viện chart
ngoài). KHÔNG đụng Cognex trực tiếp ở V1 (chỉ gọi qua `IVisionEngine`). Cùng WinForms/net48 nên có
thể nhúng trực tiếp `CogRecordDisplay`/`CogToolBlockEditV2` của Cognex sau này nếu muốn minh hoạ
sâu (Chương 5 §5.5) — không cần `WindowsFormsHost` hay viết lại control hiển thị; chưa làm ở V1.

4 dialog bổ sung (Session 6, `docs/design-notes/DESIGN_LOG.md` mục [8] — theo khoảng trống so với
RefVis-A, xem `Review_RefVis-A.md` mục 14): `FormLogin` (đăng nhập Engineer tối giản),
`FormTeach` (Ch14 §14.4.2 — chọn vùng train, gọi `TeachPatternAsync`), `FormNgImages`
(Ch14 §14.5 — xem/lọc ảnh NG theo ngày), `FormImageRetentionSettings` (Ch14 §14.5.2 — chính sách
lưu ảnh, ghi thẳng `appsettings.json`).
