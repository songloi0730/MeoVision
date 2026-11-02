# MeoVision.PlcTestReceiver (net48, console)

Công cụ thủ công, độc lập hoàn toàn với các project MeoVision khác (không reference gì, kể cả
`MeoVision.Core`) — đóng vai PLC/gateway: `TcpListener` lắng nghe `127.0.0.1:5050`, in mọi tín
hiệu Busy/Done nhận được kèm timestamp, cho gõ `t` (gửi TRIGGER) / `a` (gửi ACK) / `q` (thoát) để
tác giả tự chạy tay từng bước và xác nhận `MeoVision.WorkStation.Demo` (transport `Tcp`) hoạt động
đúng qua mạng thật, không chỉ trong 1 process console như bản `Simulated`.

Chạy: `MeoVision.PlcTestReceiver.exe [port]` (mặc định cổng `5050`, khớp
`appsettings.json` của `MeoVision.WorkStation.Demo`) — khởi động TRƯỚC khi chạy
`MeoVision.WorkStation.Demo.exe`.

Xem `docs/design-notes/DESIGN_LOG.md` mục [6] để biết lý do chọn giao thức text ASCII theo dòng và
mô hình Vision=client/PLC=server.
