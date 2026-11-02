using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using Cognex.VisionPro;
using Cognex.VisionPro.ToolBlock;
using MeoVision.Core;
using MeoVision.Core.Commands;
using MeoVision.Core.Devices;
using MeoVision.Vision.Devices;

namespace MeoVision.Vision.Commands;

/// <summary>
/// Một <c>CogToolBlock</c> đã nạp, chạy theo lệnh (Chương 20, mục 20.2). Ba việc lớp này làm mà
/// một lời gọi <c>Run()</c> trần không làm — mỗi việc chặn một lỗi đã đo được trên VisionPro thật:
/// khoá theo job, xoá output cũ trước khi chạy, bỏ tham chiếu ảnh sau khi chạy.
/// </summary>
public sealed class CogVisionJob : IVisionJob
{
    private readonly CogToolBlock _block;
    private readonly string _inputTerminal;
    private static readonly IReadOnlyDictionary<string, double> NoInputs =
        new Dictionary<string, double>();

    private readonly object _sync = new();
    private bool _disposed;

    public CogVisionJob(CogToolBlock block, string name, string inputTerminal)
    {
        Guard.ThrowIfNull(block, nameof(block));
        Guard.ThrowIfNull(name, nameof(name));
        Guard.ThrowIfNull(inputTerminal, nameof(inputTerminal));
        _block = block;
        Name = name;
        _inputTerminal = inputTerminal;
    }

    public string Name { get; }

    public JobRun Run(CameraImage image, IReadOnlyList<string> outputs) =>
        Run(image, new JobRequest(outputs, NoInputs, WithRecord: false));

    public JobRun Run(CameraImage image, JobRequest request)
    {
        Guard.ThrowIfNull(image, nameof(image));
        Guard.ThrowIfNull(request, nameof(request));
        ICogImage input = CogCameraImage.From(image);

        // Một ToolBlock không chạy được trên hai luồng cùng lúc: đo thật, 9/200 lần ra số SAI
        // mà không có exception nào. Hai lệnh dùng chung job (hai camera) phải xếp hàng ở đây.
        lock (_sync)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(Name);
            }
            Stopwatch watch = Stopwatch.StartNew();

            // Job "chỉ gán output khi tìm thấy" để lại số của lần trước kèm RunStatus = Accept
            // (đo thật). Xoá hết trước khi chạy: output nào còn null sau Run là job không gán.
            for (int i = 0; i < _block.Outputs.Count; i++)
            {
                _block.Outputs[i].Value = null;
            }
            // Tham số nhanh (Chương 22): giá trị từ product.json, đổi sang đúng kiểu của terminal.
            foreach (KeyValuePair<string, double> pair in request.Inputs)
            {
                CogToolBlockTerminal terminal = _block.Inputs[pair.Key];
                terminal.Value = Convert.ChangeType(pair.Value, terminal.ValueType,
                    CultureInfo.InvariantCulture);
            }
            _block.Inputs[_inputTerminal].Value = input;
            // Lệnh nhiều camera: mỗi ảnh thêm vào terminal riêng của nó.
            if (request.Images is not null)
            {
                foreach (KeyValuePair<string, CameraImage> extra in request.Images)
                {
                    _block.Inputs[extra.Key].Value = CogCameraImage.From(extra.Value);
                }
            }
            try
            {
                _block.Run();
                Dictionary<string, object?> values = new(StringComparer.Ordinal);
                foreach (string name in request.Outputs)
                {
                    values[name] = _block.Outputs[name].Value;
                }
                ICogRunStatus status = _block.RunStatus;
                // Record tạo NGAY, trong khoá và khi ảnh còn sống: nó trỏ vào chính ảnh vào và các
                // ảnh mới của tool ở lần chạy này — lần chạy sau không ghi đè lên nó (đo thật).
                object? record = request.WithRecord ? _block.CreateLastRunRecord() : null;
                return new JobRun(Map(status.Result), status.Message ?? string.Empty, values,
                    watch.ElapsedMilliseconds, record);
            }
            finally
            {
                // ToolBlock không giữ ảnh; record (nếu có) giữ thay, cùng DisplayFrame.
                _block.Inputs[_inputTerminal].Value = null;
                if (request.Images is not null)
                {
                    foreach (string name in request.Images.Keys)
                    {
                        _block.Inputs[name].Value = null;
                    }
                }
            }
        }
    }

    public void Save(string file)
    {
        Guard.ThrowIfNull(file, nameof(file));
        lock (_sync)
        {
            string temp = file + ".tmp";
            CogSerializer.SaveObjectToFile(_block, temp);
            if (File.Exists(file))
            {
                File.Replace(temp, file, null);
            }
            else
            {
                File.Move(temp, file);
            }
        }
    }

    public object EditSubject => _block;

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            _block.Dispose();
        }
    }

    private static JobRunStatus Map(CogToolResultConstants result) => result switch
    {
        CogToolResultConstants.Accept => JobRunStatus.Accept,
        CogToolResultConstants.Warning => JobRunStatus.Warning,
        CogToolResultConstants.Reject => JobRunStatus.Reject,
        _ => JobRunStatus.Error,
    };
}
