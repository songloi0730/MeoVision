# MeoVision.Engine.Simulated (net48)

`SimulatedVisionEngine : IVisionEngine` — không Cognex, chạy được trên mọi máy không có license
VisionPro. Đánh giá THUẦN thuật toán qua `SimulatedEvaluator` (tách khỏi phần `Random` để test được
tất định), vẽ ảnh thủ tục bằng `SyntheticPartRenderer` (GDI+, không đọc ảnh mẫu từ đĩa).
`SimulatedCameraSource : ICameraSource` cố tình đơn giản (khung xám trơn) — test camera thật là
chính, không cần mô phỏng cầu kỳ. Phải giữ song song ("simulation parity") với `MeoVision.Engine`
— xem skill `meovision-sim-parity`.
