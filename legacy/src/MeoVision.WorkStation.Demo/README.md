# MeoVision.WorkStation.Demo (net48, console)

Mô phỏng chu trình PLC↔Vision (Hình 1.2, Hình 15.1 của sách) — `PlcHandshakeSimulator` orchestrator
gọi `IPlcCommunication` cho 4 tín hiệu thô (Trigger/Busy/Done/Ack) quanh `InspectionCycleService`
thật, 2 watchdog độc lập (PLC rộng hơn `CycleTimeoutMs` nội bộ — Ch15 §15.5.2). Transport hiện có:
`SimulatedPlcCommunication` (console); bản TCP qua `127.0.0.1` đang triển khai (xem
`docs/design-notes/DESIGN_LOG.md` mục [6]).
