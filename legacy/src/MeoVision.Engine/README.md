# MeoVision.Engine (net48, x64)

DUY NHẤT project reference `Cognex.VisionPro.*` thật. `CognexVisionEngine` nạp `.vpp` bằng
`CogSerializer`, chạy `CogToolBlock`, implements `IVisionEngine` trực tiếp trong-process (KHÔNG
qua IPC — xem `docs/design-notes/DESIGN_LOG.md` mục [2] lý do bỏ IPC). `CognexCameraSource`
implements `ICameraSource` (thu ảnh GigE thật qua `CogFrameGrabberGigEs`/`ICogAcqFifo`).
