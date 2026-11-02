namespace MeoVision.Core.Contracts;

public sealed record VisionRecipe(
    string RecipeId,
    string JobName,
    string JobFilePath,
    string SampleImageFolder,
    double MinAcceptScore,
    double PartWidthMinMm,
    double PartWidthMaxMm,
    int ExpectedPadCount,
    int MaxRetries = 2,
    int CycleTimeoutMs = 2000,
    int JobLoadTimeoutMs = 10_000,
    // Tham số camera/đèn (Ch6) — chỉ ICameraSource thật (CognexCameraSource) đọc; bản giả lập bỏ
    // qua. Semi (PLC trigger phần cứng, phần mềm arm qua StartAcquire) là mặc định phổ biến nhất
    // theo sách Bảng 6.2. Đơn vị StrobeDelay/StrobePulseDuration là micro-giây, khớp
    // ICogAcqStrobeDelay.StrobeDelay/ICogAcqStrobePulseDuration.StrobePulseDuration thật (double).
    CameraTriggerMode TriggerMode = CameraTriggerMode.Semi,
    bool StrobeEnabled = false,
    double StrobeDelayUs = 0,
    double StrobePulseDurationUs = 100);
