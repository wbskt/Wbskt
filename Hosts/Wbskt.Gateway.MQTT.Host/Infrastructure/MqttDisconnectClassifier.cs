using MQTTnet.Packets;
using MQTTnet.Server;
using Wbskt.Gateway.Core.Infrastructure;
using Wbskt.Protocol;

namespace Wbskt.Gateway.MQTT.Host.Infrastructure;

/// <summary>
/// Why a client's MQTT connection ended, from what MQTTnet saw of it, in Gateway.Core's terms.
/// </summary>
public static class MqttDisconnectClassifier
{
    /// <param name="type">How MQTTnet saw the connection end.</param>
    /// <param name="userProperties">The client's DISCONNECT user properties, when it sent one.</param>
    /// <param name="packetTooBig">Whether the packet guard cut the connection for an oversized packet.</param>
    /// <param name="stopping">Whether the host is shutting down.</param>
    public static DisconnectReason Classify(
        MqttClientDisconnectType type,
        IReadOnlyCollection<MqttUserProperty>? userProperties,
        bool packetTooBig,
        bool stopping)
    {
        // The guard cuts the socket, which MQTTnet sees as an abort.
        if (packetTooBig)
        {
            return DisconnectReason.MessageTooBig;
        }

        return type switch
        {
            MqttClientDisconnectType.Takeover => DisconnectReason.Superseded,
            MqttClientDisconnectType.Clean when IsUpdating(userProperties) => DisconnectReason.Updating,
            MqttClientDisconnectType.Clean => DisconnectReason.ClientClosed,
            _ when stopping => DisconnectReason.ServerStopping,
            _ => DisconnectReason.Aborted
        };
    }

    // A user property rather than the reason string, which MQTTnet does not pass on to the server.
    private static bool IsUpdating(IReadOnlyCollection<MqttUserProperty>? userProperties) =>
        userProperties?.Any(p => p.Name == MqttBinding.CloseReasonProperty && p.ReadValueAsString() == MqttBinding.UpdatingReason) == true;
}
