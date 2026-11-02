using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MeoVision.App.Devices;
using MeoVision.App.Devices.Lights;
using MeoVision.Core.Devices;
using MeoVision.Core.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MeoVision.App.Tests;

public class MachineConfigTests
{
    private static readonly IDeviceFactory[] Factories =
    {
        new StubCameraFactory("Folder"), new StubCameraFactory("CognexGigE"),
        new AsciiLightFactory(),
    };

    [Fact]
    public void Valid_File_Loads_Without_Problems()
    {
        MachineConfigResult result = Load(@"{
  ""schemaVersion"": 1, ""machineName"": ""AOI-02"",
  ""cameras"": [ { ""role"": ""TopCam"", ""type"": ""Folder"", ""settings"": { } } ],
  ""lights"": [ { ""role"": ""MainLight"", ""type"": ""AsciiLight"",
                 ""settings"": { ""port"": ""COM3"", ""setCommand"": ""S{channel}{value:000}"" } } ]
}");

        result.Problems.Should().BeEmpty();
        result.IsValid.Should().BeTrue();
        result.Config!.Cameras.Single().Role.Should().Be("TopCam");
    }

    [Fact]
    public void Misspelled_Field_Is_An_Error_Not_Silently_Ignored()
    {
        // "camras" thay vì "cameras": nếu bỏ qua im lặng, máy lên mà không có camera nào.
        MachineConfigResult result = Load(@"{ ""schemaVersion"": 1, ""machineName"": ""M"",
            ""camras"": [] }");

        result.Problems.Single().Key.Should().Be("machine.invalidJson");
    }

    [Fact]
    public void All_Problems_Are_Reported_In_One_Pass()
    {
        MachineConfigResult result = Load(@"{
  ""schemaVersion"": 2, ""machineName"": """",
  ""cameras"": [ { ""role"": ""TopCam"", ""type"": ""Folder"" },
                 { ""role"": ""topcam"", ""type"": ""Basler"" } ],
  ""lights"": [ { ""role"": ""MainLight"", ""type"": ""AsciiLight"",
                 ""settings"": { ""channels"": ""eight"", ""setCommand"": ""S{channel"" } } ]
}");

        result.Problems.Select(p => p.Key).Should().BeEquivalentTo(
            "machine.schemaVersion", "machine.nameEmpty",
            "machine.role.duplicate",                 // vai trò không phân biệt hoa thường
            "machine.type.unknown",                   // kèm danh sách loại đang có
            "machine.setting.notNumber", "machine.setting.missing",   // channels, port
            "machine.setting.badTemplate");           // "{channel" thiếu ngoặc đóng
        result.Problems.Single(p => p.Key == "machine.type.unknown").Args[2]
            .Should().Be("Folder, CognexGigE");
    }

    [Fact]
    public void Missing_File_Is_Reported_With_Its_Path()
    {
        MachineConfigResult result = new MachineConfigLoader(Factories).Load(@"C:\nowhere\m.json");

        result.IsValid.Should().BeFalse();
        result.Problems.Single().Key.Should().Be("machine.missing");
    }

    private static MachineConfigResult Load(string json)
    {
        string file = Path.Combine(Path.GetTempPath(), "machine-" + Guid.NewGuid() + ".json");
        File.WriteAllText(file, json, Encoding.UTF8);
        try
        {
            return new MachineConfigLoader(Factories).Load(file);
        }
        finally
        {
            File.Delete(file);
        }
    }

    private sealed class StubCameraFactory : IDeviceFactory
    {
        public StubCameraFactory(string type) => Type = type;

        public DeviceKind Kind => DeviceKind.Camera;

        public string Type { get; }

        public IReadOnlyList<Msg> Validate(DeviceConfig config) => Array.Empty<Msg>();

        public IDevice Create(DeviceConfig config, string configDirectory) =>
            new FakeCamera(config.Role);
    }
}

public class DeviceManagerTests
{
    private static DeviceManager Create(FakeCamera camera, List<DeviceStatusEventArgs> events)
    {
        DeviceManager manager = new(new DeviceManagerOptions
        {
            OpenTimeout = TimeSpan.FromMilliseconds(500),
            GrabTimeout = TimeSpan.FromMilliseconds(200),
            RetryDelayMin = TimeSpan.FromMilliseconds(50),
        }, NullLogger<DeviceManager>.Instance);
        manager.StatusChanged += (_, e) => events.Add(e);
        manager.Add(camera);
        return manager;
    }

    [Fact]
    public async Task Failed_Open_Does_Not_Throw_And_Leaves_Device_Faulted()
    {
        List<DeviceStatusEventArgs> events = new();
        FakeCamera camera = new("TopCam") { FailOpen = true };
        using DeviceManager manager = Create(camera, events);

        IReadOnlyList<DeviceOpenResult> results = await manager.OpenAllAsync();

        results.Single().Status.Should().Be(DeviceStatus.Faulted);
        results.Single().Detail.Key.Should().Be("fake.openFailed");
        manager.StatusOf("TopCam").Should().Be(DeviceStatus.Faulted);
    }

    [Fact]
    public async Task Hung_Grab_Times_Out_Then_Watchdog_Reconnects_After_It_Returns()
    {
        List<DeviceStatusEventArgs> events = new();
        FakeCamera camera = new("TopCam");
        using DeviceManager manager = Create(camera, events);
        await manager.OpenAllAsync();

        // SDK treo, KHÔNG nghe CancellationToken — giống camera mất kết nối giữa lúc chụp.
        camera.HangGrab = new ManualResetEventSlim(false);
        Func<Task> grab = () => manager.GrabAsync("TopCam", CaptureSettings.Default);
        (await grab.Should().ThrowAsync<DeviceException>()).Which.Info.Key
            .Should().Be("device.timeout");
        manager.StatusOf("TopCam").Should().Be(DeviceStatus.Faulted);

        // Lệnh tiếp theo bị từ chối NGAY, không chờ thêm 200 ms nữa.
        DateTime start = DateTime.UtcNow;
        (await grab.Should().ThrowAsync<DeviceException>()).Which.Info.Key
            .Should().Be("device.notConnected");
        (DateTime.UtcNow - start).Should().BeLessThan(TimeSpan.FromMilliseconds(100));

        // Lời gọi cũ còn treo: watchdog KHÔNG được mở lại thiết bị dưới chân nó.
        await Task.Delay(100);
        await manager.RunWatchdogOnceAsync();
        camera.OpenCount.Should().Be(1);

        camera.HangGrab.Set();          // lời gọi cũ cuối cùng cũng trả về
        camera.HangGrab = null;
        await Task.Delay(100);
        await manager.RunWatchdogOnceAsync();

        camera.OpenCount.Should().Be(2);
        manager.StatusOf("TopCam").Should().Be(DeviceStatus.Connected);
        events.Select(e => e.Status).Should().Equal(
            DeviceStatus.Connected, DeviceStatus.Faulted, DeviceStatus.Connected);
        using CameraImage image = await manager.GrabAsync("TopCam", CaptureSettings.Default);
        image.Width.Should().Be(64);
    }

    [Fact]
    public async Task Unknown_Role_And_Wrong_Kind_Are_Clear_Errors()
    {
        using DeviceManager manager = Create(new FakeCamera("TopCam"), new());
        await manager.OpenAllAsync();

        Func<Task> unknown = () => manager.GrabAsync("SideCam", CaptureSettings.Default);
        (await unknown.Should().ThrowAsync<DeviceException>()).Which.Info.Key
            .Should().Be("device.unknownRole");
        Func<Task> wrongKind = () => manager.SetLightsAsync("TopCam",
            new[] { new LightChannelSetting(1, 100) });
        (await wrongKind.Should().ThrowAsync<DeviceException>()).Which.Info.Key
            .Should().Be("device.wrongKind");
    }
}

public class AsciiLightTests
{
    [Fact]
    public async Task Sends_Formatted_Commands_Checks_Replies_And_Skips_Unchanged_Channels()
    {
        using FakeLightController fake = new(reply: "OK");
        AsciiLightController light = Create(fake.Port, reply: "OK");
        await light.OpenAsync(CancellationToken.None);

        await light.SetAsync(new[] { new LightChannelSetting(1, 128),
            new LightChannelSetting(2, 5) }, CancellationToken.None);
        await light.SetAsync(new[] { new LightChannelSetting(1, 128),
            new LightChannelSetting(2, 0) }, CancellationToken.None);

        // Kênh 1 không đổi ở lần hai → không gửi lại.
        fake.Received.Should().Equal("SA0128#", "SB0005#", "SB0000#");
        light.Dispose();
    }

    [Fact]
    public async Task Wrong_Reply_Is_A_Device_Error()
    {
        using FakeLightController fake = new(reply: "ERR");
        AsciiLightController light = Create(fake.Port, reply: "OK");
        await light.OpenAsync(CancellationToken.None);

        Func<Task> set = () => light.SetAsync(new[] { new LightChannelSetting(1, 10) },
            CancellationToken.None);

        (await set.Should().ThrowAsync<DeviceException>()).Which.Info.Key
            .Should().Be("light.badReply");
        light.Dispose();
    }

    [Fact]
    public async Task Controller_Closing_The_Connection_Faults_The_Device()
    {
        using FakeLightController fake = new(reply: "OK");
        using DeviceManager manager = new(new DeviceManagerOptions(),
            NullLogger<DeviceManager>.Instance);
        manager.Add(Create(fake.Port, reply: "OK"));
        await manager.OpenAllAsync();

        for (int i = 0; i < 100 && fake.ClientCount == 0; i++)
        {
            await Task.Delay(10);   // chờ bộ điều khiển giả nhận xong kết nối
        }
        fake.DropClients();   // rút cáp mạng phía bộ điều khiển
        Func<Task> set = () => manager.SetLightsAsync("MainLight",
            new[] { new LightChannelSetting(1, 50) });

        await set.Should().ThrowAsync<DeviceException>();
        manager.StatusOf("MainLight").Should().Be(DeviceStatus.Faulted);
    }

    private static AsciiLightController Create(int port, string reply)
    {
        DeviceConfig config = new()
        {
            Role = "MainLight",
            Type = "AsciiLight",
            Settings = new Dictionary<string, string>
            {
                ["transport"] = "Tcp", ["host"] = "127.0.0.1", ["port"] = port.ToString(),
                ["channelNames"] = "A,B,C,D", ["setCommand"] = "S{channel}{value:0000}#",
                ["reply"] = reply, ["replyTerminator"] = "\r\n",
            },
        };
        AsciiLightFactory factory = new();
        factory.Validate(config).Should().BeEmpty();
        return (AsciiLightController)factory.Create(config, string.Empty);
    }
}

/// <summary>Bộ điều khiển đèn giả qua TCP THẬT: ghi lại lệnh, trả lời cố định.</summary>
internal sealed class FakeLightController : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly List<TcpClient> _clients = new();
    private readonly string _reply;

    public FakeLightController(string reply)
    {
        _reply = reply;
        _listener.Start();
        _ = AcceptLoopAsync();
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public List<string> Received { get; } = new();

    public int ClientCount
    {
        get
        {
            lock (_clients)
            {
                return _clients.Count;
            }
        }
    }

    public void DropClients()
    {
        lock (_clients)
        {
            _clients.ForEach(c => c.Close());
            _clients.Clear();
        }
    }

    public void Dispose()
    {
        DropClients();
        _listener.Stop();
    }

    private async Task AcceptLoopAsync()
    {
        try
        {
            while (true)
            {
                TcpClient client = await _listener.AcceptTcpClientAsync();
                lock (_clients)
                {
                    _clients.Add(client);
                }
                _ = ServeAsync(client);
            }
        }
        catch (Exception ex) when (ex is ObjectDisposedException || ex is SocketException)
        {
            // listener đã dừng
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        try
        {
            NetworkStream stream = client.GetStream();
            StringBuilder line = new();
            byte[] one = new byte[1];
            while (await stream.ReadAsync(one, 0, 1) == 1)
            {
                line.Append((char)one[0]);
                if (one[0] == (byte)'#')
                {
                    lock (Received)
                    {
                        Received.Add(line.ToString());
                    }
                    line.Clear();
                    byte[] reply = Encoding.ASCII.GetBytes(_reply + "\r\n");
                    await stream.WriteAsync(reply, 0, reply.Length);
                }
            }
        }
        catch (Exception ex) when (ex is IOException || ex is ObjectDisposedException)
        {
            // client bị đóng
        }
    }
}

/// <summary>Camera giả: điều khiển được lỗi mở và treo khi chụp.</summary>
internal sealed class FakeCamera : ICamera
{
    public FakeCamera(string role) => Role = role;

    public string Role { get; }

    public string Description => "fake";

    public bool FailOpen { get; set; }

    public ManualResetEventSlim? HangGrab { get; set; }

    public int OpenCount { get; private set; }

    public Task OpenAsync(CancellationToken ct)
    {
        OpenCount++;
        return FailOpen
            ? throw new DeviceException(Msg.Of("fake.openFailed"))
            : Task.CompletedTask;
    }

    public Task<CameraImage> GrabAsync(CaptureSettings settings, CancellationToken ct)
    {
        HangGrab?.Wait();   // chặn luồng, bỏ qua ct — đúng kiểu SDK đồng bộ
        return Task.FromResult<CameraImage>(new FakeImage());
    }

    public void Close()
    {
    }

    public void Dispose()
    {
    }

    private sealed class FakeImage : CameraImage
    {
        public FakeImage()
            : base("fake", 64, 48, DateTime.Now)
        {
        }
    }
}
