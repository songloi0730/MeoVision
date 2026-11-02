using System;
using System.Collections.Generic;
using System.Globalization;
using MeoVision.Core;
using MeoVision.Core.Commands;
using MeoVision.Core.Localization;
using MeoVision.Core.Products;

namespace MeoVision.App.Commands;

/// <summary>Kết luận của một lệnh, trước khi ghép thời gian.</summary>
public sealed record Judgement(CommandStatus Status, Msg? Error, IReadOnlyList<OutputValue> Outputs);

/// <summary>
/// Từ kết quả thô của job ra kết luận gửi máy (Chương 20, mục 20.5): cộng bù, định dạng, kiểm giới
/// hạn. Hàm thuần — không thiết bị, không VisionPro — nên mọi quy tắc ở đây có unit test.
/// </summary>
public static class ResultJudge
{
    public static Judgement Judge(CommandConfig command, JobRun run)
    {
        Guard.ThrowIfNull(command, nameof(command));
        Guard.ThrowIfNull(run, nameof(run));
        if (run.Status == JobRunStatus.Error)
        {
            return new Judgement(CommandStatus.Error,
                Msg.Of("cmd.jobError", command.Job, run.Message), Array.Empty<OutputValue>());
        }

        List<OutputValue> values = new();
        Msg? firstProblem = null;
        bool allInLimits = true;
        foreach (OutputConfig output in command.Outputs)
        {
            run.Outputs.TryGetValue(output.Terminal, out object? raw);
            OutputValue value = Convert(output, raw, out Msg? problem);
            firstProblem ??= problem;
            allInLimits &= value.InLimits;
            values.Add(value);
        }

        // Thứ tự quan trọng: job tự kết luận Reject (không tìm thấy chi tiết...) thì thiếu số là
        // chuyện bình thường → NG. Job nói Accept mà thiếu số → job hoặc bảng lệnh sai → LỖI,
        // không được đoán là OK hay NG.
        if (run.Status == JobRunStatus.Reject)
        {
            return new Judgement(CommandStatus.Ng, null, values);
        }
        if (firstProblem is not null)
        {
            return new Judgement(CommandStatus.Error, firstProblem, values);
        }
        return new Judgement(allInLimits ? CommandStatus.Ok : CommandStatus.Ng, null, values);
    }

    private static OutputValue Convert(OutputConfig output, object? raw, out Msg? problem)
    {
        problem = null;
        switch (raw)
        {
            case null:
                problem = Msg.Of("cmd.outputMissing", output.Terminal);
                return new OutputValue(output.Name, null, string.Empty, false);
            case string text:
                return new OutputValue(output.Name, null, text, true);
            case bool flag:
                return new OutputValue(output.Name, flag ? 1 : 0, flag ? "1" : "0", true);
            case not IConvertible:
                problem = Msg.Of("cmd.outputType", output.Terminal, raw.GetType().Name);
                return new OutputValue(output.Name, null, string.Empty, false);
        }

        double value = System.Convert.ToDouble(raw, CultureInfo.InvariantCulture);
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            problem = Msg.Of("cmd.outputInvalid", output.Terminal, value);
            return new OutputValue(output.Name, null, string.Empty, false);
        }

        // Văn hoá BẤT BIẾN: máy Windows đặt vi-VN in 130.98 thành "130,98" — dấu phẩy đó vỡ
        // bản tin ngăn cách bằng dấu phẩy và PLC đọc thành hai số.
        string formatted = (value + output.Offset).ToString(output.Format,
            CultureInfo.InvariantCulture);
        // Xét giới hạn trên SỐ MÁY NHẬN (đã làm tròn), không phải số thô: Upper 0.050 mà gửi
        // "0.050" kèm NG thì người đọc log không hiểu vì sao.
        double sent = double.TryParse(formatted, NumberStyles.Float | NumberStyles.AllowThousands,
            CultureInfo.InvariantCulture, out double parsed)
            ? parsed
            : value + output.Offset;
        bool inLimits = !(sent < output.Lower) && !(sent > output.Upper);
        return new OutputValue(output.Name, sent, formatted, inLimits);
    }
}
