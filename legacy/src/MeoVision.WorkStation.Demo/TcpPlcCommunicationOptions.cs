namespace MeoVision.WorkStation.Demo;

public sealed class TcpPlcCommunicationOptions
{
    public string Host { get; set; } = "127.0.0.1";

    public int Port { get; set; } = 5050;

    public int ConnectTimeoutMs { get; set; } = 5000;
}
