using System;
using System.IO;
using Cognex.VisionPro;
using Cognex.VisionPro.ImageFile;
using MeoVision.Core;
using MeoVision.Core.Devices;
using MeoVision.Core.Storage;
using MeoVision.Vision.Devices;

namespace MeoVision.Vision.Storage;

/// <summary>
/// Lưu ảnh bằng <see cref="CogImageFile"/> (Chương 23, mục 23.4): BMP không nén — ghi nhanh, không
/// mất dữ liệu, và chính "camera đọc thư mục" (Chương 18) đọc lại được để chạy lại job trên ảnh NG.
/// </summary>
public sealed class CogImageStore : IImageStore, IDisposable
{
    private readonly Lazy<AnnotatedImageRenderer> _renderer = new(() => new AnnotatedImageRenderer());

    public string Extension => ".bmp";

    public bool SaveAnnotated(object record, string preferredKey, string file)
    {
        Guard.ThrowIfNull(record, nameof(record));
        return record is ICogRecord cogRecord &&
               _renderer.Value.Render(cogRecord, preferredKey ?? string.Empty, file);
    }

    public void Dispose()
    {
        if (_renderer.IsValueCreated)
        {
            _renderer.Value.Dispose();
        }
    }

    public void Save(CameraImage image, string file)
    {
        Guard.ThrowIfNull(image, nameof(image));
        Guard.ThrowIfNull(file, nameof(file));
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        string temp = file + ".tmp" + Extension;   // CogImageFile chọn định dạng theo đuôi file
        CogImageFile writer = new();
        try
        {
            writer.Open(temp, CogImageFileModeConstants.Write);
            writer.Append(CogCameraImage.From(image));
        }
        finally
        {
            writer.Close();
            ((IDisposable)writer).Dispose();
        }
        // Đổi tên sau khi ghi xong: thư mục ảnh không bao giờ chứa file ghi dở (mất điện, đầy đĩa).
        if (File.Exists(file))
        {
            File.Delete(file);
        }
        File.Move(temp, file);
    }

    public CameraImage Load(string file)
    {
        Guard.ThrowIfNull(file, nameof(file));
        CogImageFile reader = new();
        try
        {
            reader.Open(file, CogImageFileModeConstants.Read);
            ICogImage image = reader[0];
            return new CogCameraImage(image, Path.GetFileName(file), File.GetLastWriteTime(file));
        }
        finally
        {
            reader.Close();
            ((IDisposable)reader).Dispose();
        }
    }
}
