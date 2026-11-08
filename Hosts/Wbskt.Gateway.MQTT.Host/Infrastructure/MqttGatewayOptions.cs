using Wbskt.Protocol;

namespace Wbskt.Gateway.MQTT.Host.Infrastructure;

/// <summary>The MQTT listener (<c>Mqtt</c>).</summary>
public sealed class MqttGatewayOptions
{
    public const string SectionName = "Mqtt";

    /// <summary>
    /// Room for a topic, a packet id and MQTT 5 properties on top of the message itself. The message
    /// is still held to <see cref="ClientProtocol.MaxMessageBytes"/> where every transport meets.
    /// </summary>
    public const int PacketOverheadBytes = 1024;

    /// <summary>Plain TCP. TLS ends in front of the host (Traefik's mqtts entrypoint on 8883).</summary>
    public int Port { get; set; } = 1883;

    /// <summary>Messages a client may have published that its session has not read yet, before its packets wait.</summary>
    public int InboxCapacity { get; set; } = 32;

    /// <summary>The largest packet a client may send: one message and its MQTT framing.</summary>
    public int MaxPacketBytes => ClientProtocol.MaxMessageBytes + PacketOverheadBytes;
}
