using Xunit;

// xUnit mặc định chạy các LỚP test song song. Với VisionPro, lần chạm đầu tiên của tiến trình chạy
// song song trên nhiều luồng có thể ném ArgumentNullException ("pUnk") hoặc làm chết tiến trình
// test (đo thật — xem VisionProLock). Ứng dụng đã tuần tự hoá bằng khoá; test tích hợp thì chạy
// tuần tự luôn cho chắc — vài giây chậm hơn đổi lấy kết quả không chập chờn.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
