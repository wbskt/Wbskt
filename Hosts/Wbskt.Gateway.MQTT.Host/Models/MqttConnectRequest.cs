using MQTTnet.Formatter;

namespace Wbskt.Gateway.MQTT.Host.Models;

/// <summary>What a client's CONNECT says about who it is: its protocol, client id and credentials.</summary>
public sealed record MqttConnectRequest(MqttProtocolVersion ProtocolVersion, string? ClientId, string? UserName, string? Password);
