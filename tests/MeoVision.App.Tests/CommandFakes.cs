using System;
using System.Collections.Generic;
using MeoVision.Core.Commands;
using MeoVision.Core.Devices;
using MeoVision.Core.Localization;

namespace MeoVision.App.Tests;

/// <summary>Job giả: trả output cấu hình sẵn, ghi lại đã bị đóng chưa (Chương 20).</summary>
internal sealed class FakeVisionJob : IVisionJob
{
    public FakeVisionJob(string name) => Name = name;

    public string Name { get; }

    public bool Disposed { get; private set; }

    public Func<CameraImage, JobRun> Behavior { get; set; } = _ =>
        new JobRun(JobRunStatus.Accept, string.Empty, new Dictionary<string, object?>(), 0);

    /// <summary>Số lần Run chạy CHỒNG nhau nhiều nhất — job thật không chịu được &gt; 1.</summary>
    public int MaxConcurrent { get; private set; }

    private int _running;

    /// <summary>Yêu cầu của lần chạy gần nhất — để kiểm tham số nhanh và cờ record.</summary>
    public JobRequest? LastRequest { get; private set; }

    /// <summary>Record giả gắn vào JobRun khi được hỏi record.</summary>
    public object Record { get; } = new();

    public List<string> SavedFiles { get; } = new();

    public object EditSubject => this;

    public JobRun Run(CameraImage image, IReadOnlyList<string> outputs) =>
        Run(image, new JobRequest(outputs, new Dictionary<string, double>(), WithRecord: false));

    public JobRun Run(CameraImage image, JobRequest request)
    {
        LastRequest = request;
        JobRun run = RunCore(image);
        return request.WithRecord ? run with { Record = Record } : run;
    }

    public void Save(string file)
    {
        System.IO.File.WriteAllText(file, Name);
        SavedFiles.Add(file);
    }

    private JobRun RunCore(CameraImage image)
    {
        int now = System.Threading.Interlocked.Increment(ref _running);
        MaxConcurrent = Math.Max(MaxConcurrent, now);
        try
        {
            return Behavior(image);
        }
        finally
        {
            System.Threading.Interlocked.Decrement(ref _running);
        }
    }

    public void Dispose() => Disposed = true;
}

/// <summary>Bộ nạp hiệu chuẩn giả: mã nào có trong <see cref="Errors"/> thì báo lỗi đó.</summary>
internal sealed class FakeCalibrationLoader : ICalibrationLoader
{
    public Dictionary<string, Msg> Errors { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<FakeCalibration> Created { get; } = new();

    public CalibrationLoad Load(string id)
    {
        if (Errors.TryGetValue(id, out Msg? error))
        {
            return new CalibrationLoad(null, error);
        }
        FakeCalibration calibration = new(id);
        Created.Add(calibration);
        return new CalibrationLoad(calibration, null);
    }
}

internal sealed class FakeCalibration : ICalibration
{
    public FakeCalibration(string id) => Id = id;

    public string Id { get; }

    public bool Disposed { get; private set; }

    public void Apply(CameraImage image)
    {
    }

    public void Dispose() => Disposed = true;
}
