namespace Wbskt.Gateway.Core.Telemetry;

/// <summary>The transport a gateway host serves devices over, such as <c>websocket</c>; tags its metrics.</summary>
public sealed record GatewayTransport(string Name)
{
    public static readonly GatewayTransport WebSocket = new("websocket");
}
