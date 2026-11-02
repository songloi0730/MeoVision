using System;
using System.Collections.Generic;
using MeoVision.Core;
using MeoVision.Core.Commands;
using MeoVision.Core.Products;

namespace MeoVision.App.Products;

/// <summary>
/// Sản phẩm đã qua kiểm tra, sẵn sàng chạy (Chương 20, mục 20.2): cấu hình + các job ĐÃ MỞ + các
/// hiệu chuẩn đã nạp — đúng những đối tượng bộ nạp vừa kiểm. Lệnh đang chạy "mượn" sản phẩm qua
/// <see cref="Acquire"/>; chuyển sản phẩm chỉ giải phóng job cũ khi lệnh cuối cùng trả lại.
/// </summary>
public sealed class LoadedProduct
{
    private readonly Dictionary<string, IVisionJob> _jobs;
    private readonly Dictionary<string, ICalibration> _calibrations;
    private readonly object _sync = new();
    private int _leases;
    private bool _retired;
    private bool _released;

    /// <param name="version">Phiên bản = mã băm nội dung các file lúc nạp (Chương 23).</param>
    public LoadedProduct(ProductConfig config, IReadOnlyDictionary<string, IVisionJob> jobs,
        IReadOnlyDictionary<string, ICalibration> calibrations, string version = "")
    {
        Guard.ThrowIfNull(config, nameof(config));
        Guard.ThrowIfNull(jobs, nameof(jobs));
        Guard.ThrowIfNull(calibrations, nameof(calibrations));
        Config = config;
        Version = version ?? string.Empty;
        _jobs = new Dictionary<string, IVisionJob>(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, IVisionJob> pair in jobs)
        {
            _jobs[pair.Key] = pair.Value;
        }
        _calibrations = new Dictionary<string, ICalibration>(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, ICalibration> pair in calibrations)
        {
            _calibrations[pair.Key] = pair.Value;
        }
    }

    public ProductConfig Config { get; }

    /// <summary>
    /// 12 ký tự đầu của SHA-256 trên danh sách "file → mã băm" của sản phẩm (cùng tập file với bản
    /// sao lưu, Chương 19). Cùng nội dung → cùng phiên bản, trên mọi máy.
    /// </summary>
    public string Version { get; }

    /// <summary>Job theo tên file — bộ nạp đã bảo đảm mọi lệnh đều có.</summary>
    public IVisionJob Job(string file) => _jobs[file];

    /// <summary>Hiệu chuẩn theo mã; null khi lệnh không dùng hiệu chuẩn.</summary>
    public ICalibration? Calibration(string id) =>
        id.Length == 0 ? null : _calibrations[id];

    /// <summary>
    /// Mượn sản phẩm cho một lệnh. Trả null nếu sản phẩm đã bị thay — người gọi lấy sản phẩm mới.
    /// </summary>
    public IDisposable? Acquire()
    {
        lock (_sync)
        {
            if (_retired)
            {
                return null;
            }
            _leases++;
            return new Lease(this);
        }
    }

    /// <summary>
    /// Sản phẩm không còn nhận lệnh mới. Job và hiệu chuẩn được giải phóng ngay nếu không lệnh
    /// nào đang mượn, nếu không thì khi lệnh cuối trả lại.
    /// </summary>
    public void Retire()
    {
        lock (_sync)
        {
            _retired = true;
            if (_leases > 0)
            {
                return;
            }
        }
        Release();
    }

    private void Return()
    {
        lock (_sync)
        {
            _leases--;
            if (!_retired || _leases > 0)
            {
                return;
            }
        }
        Release();
    }

    private void Release()
    {
        lock (_sync)
        {
            if (_released)
            {
                return;
            }
            _released = true;
        }
        foreach (IVisionJob job in _jobs.Values)
        {
            job.Dispose();
        }
        foreach (ICalibration calibration in _calibrations.Values)
        {
            calibration.Dispose();
        }
    }

    private sealed class Lease : IDisposable
    {
        private LoadedProduct? _owner;

        public Lease(LoadedProduct owner) => _owner = owner;

        public void Dispose()
        {
            // Trả đúng một lần, kể cả khi Dispose bị gọi hai lần.
            System.Threading.Interlocked.Exchange(ref _owner, null)?.Return();
        }
    }
}
