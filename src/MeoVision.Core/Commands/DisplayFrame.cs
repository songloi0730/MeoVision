using System;
using System.Collections.Generic;
using System.Threading;
using MeoVision.Core.Devices;

namespace MeoVision.Core.Commands;

/// <summary>
/// Ảnh của một lần chạy lệnh cùng record hiển thị của job (Chương 22, mục 22.2). Record trỏ vào
/// chính ảnh đó — ảnh bị Dispose thì record hỏng (Chương 20, Bảng 20.3) — nên hai thứ đi chung
/// và ảnh chỉ được trả khi NGƯỜI GIỮ CUỐI CÙNG trả. Màn chính, vòng ảnh gần nhất và chạy kèm bản
/// nháp đều giữ cùng một ảnh bằng <see cref="Share"/>; mỗi người giữ tự gọi Dispose.
/// </summary>
public sealed class DisplayFrame : IDisposable
{
    private readonly Holder _holder;
    private int _released;

    /// <param name="record">Record của job (<c>ICogRecord</c> ở tầng Vision/UI); null = không vẽ.
    /// </param>
    public DisplayFrame(CameraImage image, object? record)
        : this(new Holder(Guard.NotNull(image, nameof(image)), Array.Empty<CameraImage>()), record)
    {
    }

    /// <param name="extras">Ảnh của camera thêm (lệnh nhiều camera): record có thể trỏ vào chúng
    /// nên chúng sống cùng ảnh chính và được trả cùng lúc.</param>
    public DisplayFrame(CameraImage image, object? record, IReadOnlyList<CameraImage> extras)
        : this(new Holder(Guard.NotNull(image, nameof(image)),
            Guard.NotNull(extras, nameof(extras))), record)
    {
    }

    private DisplayFrame(Holder holder, object? record)
    {
        _holder = holder;
        Record = record;
    }

    public CameraImage Image => _holder.Image;

    /// <summary>Ảnh của camera thêm, theo thứ tự trong lệnh.</summary>
    public IReadOnlyList<CameraImage> Extras => _holder.Extras;

    public object? Record { get; }

    /// <summary>Thêm một người giữ. Ảnh chỉ được trả khi mọi bản Share đã Dispose.</summary>
    public DisplayFrame Share() => ShareWith(Record);

    /// <summary>
    /// Cùng ảnh, record khác — kết quả bản nháp chạy trên ảnh này (chạy kèm, Chương 22).
    /// </summary>
    public DisplayFrame ShareWith(object? record)
    {
        if (Volatile.Read(ref _released) != 0)
        {
            throw new ObjectDisposedException(nameof(DisplayFrame));
        }
        Interlocked.Increment(ref _holder.Count);
        return new DisplayFrame(_holder, record);
    }

    public void Dispose()
    {
        // Mỗi bản chỉ trả một lần, kể cả khi Dispose bị gọi hai lần.
        if (Interlocked.Exchange(ref _released, 1) == 0 &&
            Interlocked.Decrement(ref _holder.Count) == 0)
        {
            _holder.Image.Dispose();
            foreach (CameraImage extra in _holder.Extras)
            {
                extra.Dispose();
            }
        }
    }

    private sealed class Holder
    {
        public Holder(CameraImage image, IReadOnlyList<CameraImage> extras)
        {
            Image = image;
            Extras = extras;
        }

        public CameraImage Image { get; }

        public IReadOnlyList<CameraImage> Extras { get; }

        public int Count = 1;
    }
}
