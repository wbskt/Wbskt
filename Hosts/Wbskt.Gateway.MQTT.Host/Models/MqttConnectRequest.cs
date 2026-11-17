using MQTTnet.Formatter;

namespace Wbskt.Gateway.MQTT.Host.Models;

/// <summary>What a client's CONNECT says about who it is (its protocol, client id and credentials) and its keep alive.</summary>
/// <param name="KeepAliveSeconds">The keep alive it asked for; 0 or absent is none.</param>
public sealed record MqttConnectRequest(MqttProtocolVersion ProtocolVersion, string? ClientId, string? UserName, string? Password, int? KeepAliveSeconds = null);
