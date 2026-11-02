using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MeoVision.App.Comm;
using MeoVision.Core;
using MeoVision.Core.Devices;
using MeoVision.Core.Localization;
using Newtonsoft.Json;

namespace MeoVision.App.Devices;

/// <summary>Kết quả nạp <c>machine.json</c>: cấu hình (nếu đọc được) và MỌI lỗi tìm thấy.</summary>
public sealed record MachineConfigResult(MachineConfig? Config, IReadOnlyList<Msg> Problems)
{
    public bool IsValid => Config is not null && Problems.Count == 0;
}

/// <summary>
/// Đọc và kiểm toàn bộ <c>machine.json</c> TRƯỚC khi mở thiết bị nào (Chương 18, mục 18.2): cú
/// pháp, phiên bản schema, vai trò trùng/rỗng, loại thiết bị không có factory, thiết lập riêng của
/// từng loại. Lỗi gõ sai tên trường cũng bị bắt (MissingMemberHandling.Error) — nếu không, trường
/// "Camras" sẽ bị bỏ qua im lặng và máy lên mà không có camera nào.
/// </summary>
public sealed class MachineConfigLoader
{
    private static readonly JsonSerializerSettings JsonSettings = new()
    {
        MissingMemberHandling = MissingMemberHandling.Error,
    };

    private readonly IReadOnlyList<IDeviceFactory> _factories;

    public MachineConfigLoader(IEnumerable<IDeviceFactory> factories)
    {
        Guard.ThrowIfNull(factories, nameof(factories));
        _factories = factories.ToList();
    }

    public MachineConfigResult Load(string file)
    {
        Guard.ThrowIfNull(file, nameof(file));
        if (!File.Exists(file))
        {
            return Fail(Msg.Of("machine.missing", file));
        }

        MachineConfig? config;
        try
        {
            config = JsonConvert.DeserializeObject<MachineConfig>(
                File.ReadAllText(file, Encoding.UTF8), JsonSettings);
        }
        catch (JsonException ex)
        {
            // Thông báo của Json.NET có số dòng/cột — giữ nguyên để người sửa file tìm ra chỗ sai.
            return Fail(Msg.Of("machine.invalidJson", file, ex.Message));
        }
        if (config is null)
        {
            return Fail(Msg.Of("machine.invalidJson", file, "empty"));
        }

        List<Msg> problems = new();
        if (config.SchemaVersion != MachineConfig.CurrentSchemaVersion)
        {
            problems.Add(Msg.Of("machine.schemaVersion", config.SchemaVersion,
                MachineConfig.CurrentSchemaVersion));
        }
        if (config.MachineName.Trim().Length == 0)
        {
            problems.Add(Msg.Of("machine.nameEmpty"));
        }

        HashSet<string> roles = new(StringComparer.OrdinalIgnoreCase);
        CheckDevices(config.Cameras, DeviceKind.Camera, roles, problems);
        CheckDevices(config.Lights, DeviceKind.Light, roles, problems);
        CheckChannels(config.Channels, problems);
        return new MachineConfigResult(config, problems);
    }

    // Chương 19: mã kênh duy nhất; Chương 21: thiết lập riêng từng kiểu (cổng, địa chỉ PLC...).
    private static void CheckChannels(List<ChannelConfig> channels, List<Msg> problems)
    {
        HashSet<string> ids = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < channels.Count; i++)
        {
            ChannelConfig channel = channels[i];
            if (channel.Id.Trim().Length == 0)
            {
                problems.Add(Msg.Of("machine.channel.idEmpty", i + 1));
            }
            else if (!ids.Add(channel.Id))
            {
                problems.Add(Msg.Of("machine.channel.duplicate", channel.Id));
            }
            if (channel.Type.Trim().Length == 0)
            {
                problems.Add(Msg.Of("machine.channel.typeEmpty", channel.Id));
            }
            else
            {
                ChannelHost.Validate(channel, problems);
            }
        }
    }

    /// <summary>Factory cho một thiết bị đã qua kiểm tra — dùng khi tạo thiết bị.</summary>
    public IDeviceFactory FactoryFor(DeviceKind kind, string type) =>
        _factories.First(f => f.Kind == kind &&
                              string.Equals(f.Type, type, StringComparison.OrdinalIgnoreCase));

    private void CheckDevices(List<DeviceConfig> devices, DeviceKind kind, HashSet<string> roles,
        List<Msg> problems)
    {
        for (int i = 0; i < devices.Count; i++)
        {
            DeviceConfig device = devices[i];
            if (device.Role.Trim().Length == 0)
            {
                problems.Add(Msg.Of("machine.role.empty", Msg.Of("device.kind." + kind), i + 1));
                continue;
            }
            if (!roles.Add(device.Role))
            {
                problems.Add(Msg.Of("machine.role.duplicate", device.Role));
            }

            IDeviceFactory? factory = _factories.FirstOrDefault(f => f.Kind == kind &&
                string.Equals(f.Type, device.Type, StringComparison.OrdinalIgnoreCase));
            if (factory is null)
            {
                string known = string.Join(", ",
                    _factories.Where(f => f.Kind == kind).Select(f => f.Type));
                problems.Add(Msg.Of("machine.type.unknown", device.Role, device.Type, known));
                continue;
            }
            problems.AddRange(factory.Validate(device));
        }
    }

    private static MachineConfigResult Fail(Msg problem) => new(null, new[] { problem });
}
