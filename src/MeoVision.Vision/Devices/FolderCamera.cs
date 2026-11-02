using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cognex.VisionPro;
using Cognex.VisionPro.ImageFile;
using MeoVision.Core;
using MeoVision.Core.Devices;
using MeoVision.Core.Localization;

namespace MeoVision.Vision.Devices;

/// <summary>
/// "Camera" đọc ảnh đã lưu trong một thư mục (Chương 18, mục 18.4) — thay NGUỒN ẢNH, không thay
/// thuật toán: job .vpp phía sau chạy thật. Đọc bằng <see cref="CogImageFile"/>, nên một file có
/// thể chứa nhiều ảnh (.idb, .cdb, .tif nhiều trang) — camera trả lần lượt TỪNG ẢNH, theo thứ tự
/// tên file. Dùng khi phát triển không có máy, khi chạy lại bộ ảnh NG, và khi nghiệm thu bằng bộ
/// ảnh golden (Chương 16, mục 16.3).
/// </summary>
public sealed class FolderCamera : ICamera
{
    private readonly string _folder;
    private readonly IReadOnlyList<string> _patterns;
    private readonly bool _loop;
    private readonly bool _toGrey;
    private List<string> _files = new();
    private int _fileIndex;
    private int _imageIndex;
    private CogImageFile? _current;

    public FolderCamera(string role, string folder, IReadOnlyList<string> patterns, bool loop,
        bool toGrey)
    {
        Guard.ThrowIfNull(role, nameof(role));
        Guard.ThrowIfNull(folder, nameof(folder));
        Guard.ThrowIfNull(patterns, nameof(patterns));
        Role = role;
        _folder = folder;
        _patterns = patterns;
        _loop = loop;
        _toGrey = toGrey;
    }

    public string Role { get; }

    public string Description => "Folder " + _folder;

    public Task OpenAsync(CancellationToken ct)
    {
        Close();
        if (!Directory.Exists(_folder))
        {
            throw new DeviceException(Msg.Of("camera.folder.notFound", Role, _folder));
        }
        _files = _patterns
            .SelectMany(p => Directory.GetFiles(_folder, p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (_files.Count == 0)
        {
            throw new DeviceException(Msg.Of("camera.folder.empty", Role, _folder,
                string.Join(";", _patterns)));
        }
        _fileIndex = -1;
        return Task.CompletedTask;
    }

    public Task<CameraImage> GrabAsync(CaptureSettings settings, CancellationToken ct)
    {
        // Exposure của lệnh không có ý nghĩa với ảnh đã chụp — bỏ qua, không phải lỗi.
        int emptyFiles = 0;
        while (_current is null || _imageIndex >= _current.Count)
        {
            CloseCurrent();
            if (++_fileIndex >= _files.Count)
            {
                if (!_loop)
                {
                    throw new DeviceException(Msg.Of("camera.folder.end", Role, _files.Count));
                }
                _fileIndex = 0;
            }
            if (emptyFiles++ > _files.Count)
            {
                throw new DeviceException(Msg.Of("camera.folder.empty", Role, _folder,
                    string.Join(";", _patterns)));
            }
            _current = new CogImageFile();
            _current.Open(_files[_fileIndex], CogImageFileModeConstants.Read);
            _imageIndex = 0;
        }

        int index = _imageIndex++;
        ICogImage image = _current[index];
        if (_toGrey && image is CogImage24PlanarColor)
        {
            // .jpg và .tif màu đọc ra CogImage24PlanarColor; job xám cần CogImage8Grey.
            image = CogImageConvert.GetIntensityImage(image, 0, 0, image.Width, image.Height);
        }
        string source = Path.GetFileName(_files[_fileIndex]) +
                        (_current.Count > 1 ? "#" + index : string.Empty);
        return Task.FromResult<CameraImage>(new CogCameraImage(image, source, DateTime.Now));
    }

    public void Close()
    {
        CloseCurrent();
        _files = new List<string>();
    }

    public void Dispose() => Close();

    private void CloseCurrent()
    {
        if (_current is not null)
        {
            _current.Close();
            ((IDisposable)_current).Dispose();   // IDisposable cài tường minh
            _current = null;
        }
    }
}
