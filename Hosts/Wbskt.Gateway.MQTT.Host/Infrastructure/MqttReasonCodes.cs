using MQTTnet.Protocol;
using Wbskt.Gateway.Core.Infrastructure;
using Wbskt.Gateway.Core.Services;

namespace Wbskt.Gateway.MQTT.Host.Infrastructure;

/// <summary>
/// The MQTT reason codes a client receives when this host refuses or ends its connection. These are
/// part of the client protocol (Docs/Client.Protocol.MQTT.md); SDKs act on them. MQTT 3.1.1 carries
/// none on DISCONNECT, so a 3.1.1 client sees only the connection drop.
/// </summary>
public static class MqttReasonCodes
{
    /// <summary>The CONNACK for a client refused at admission.</summary>
    public static MqttConnectReasonCode For(AdmissionRefusal refusal) => refusal switch
    {
        AdmissionRefusal.Revoked => MqttConnectReasonCode.NotAuthorized,
        _ => MqttConnectReasonCode.BadUserNameOrPassword
    };

    /// <summary>The DISCONNECT for any end of a connection this host closes.</summary>
    public static MqttDisconnectReasonCode For(DisconnectReason reason) => reason switch
    {
        DisconnectReason.Superseded => MqttDisconnectReasonCode.SessionTakenOver,
        DisconnectReason.Revoked => MqttDisconnectReasonCode.NotAuthorized,
        DisconnectReason.Throttled => MqttDisconnectReasonCode.MessageRateTooHigh,
        DisconnectReason.MessageTooBig => MqttDisconnectReasonCode.PacketTooLarge,
        DisconnectReason.ServerStopping => MqttDisconnectReasonCode.ServerShuttingDown,
        DisconnectReason.Error => MqttDisconnectReasonCode.ImplementationSpecificError,
        _ => MqttDisconnectReasonCode.NormalDisconnection
    };
}
