using MQTTnet.Protocol;
using Wbskt.Gateway.Core.Infrastructure;
using Wbskt.Gateway.Core.Services;
using Wbskt.Gateway.MQTT.Host.Infrastructure;

namespace Wbskt.Host.Tests.Gateway.Mqtt;

/// <summary>The reason codes are part of the client protocol (Docs/Client.Protocol.MQTT.md); SDKs act on them.</summary>
public sealed class MqttReasonCodesTests
{
    [Theory]
    [InlineData(DisconnectReason.Superseded, MqttDisconnectReasonCode.SessionTakenOver)]
    [InlineData(DisconnectReason.Revoked, MqttDisconnectReasonCode.NotAuthorized)]
    [InlineData(DisconnectReason.Throttled, MqttDisconnectReasonCode.MessageRateTooHigh)]
    [InlineData(DisconnectReason.MessageTooBig, MqttDisconnectReasonCode.PacketTooLarge)]
    [InlineData(DisconnectReason.ServerStopping, MqttDisconnectReasonCode.ServerShuttingDown)]
    [InlineData(DisconnectReason.Error, MqttDisconnectReasonCode.ImplementationSpecificError)]
    [InlineData(DisconnectReason.ClientClosed, MqttDisconnectReasonCode.NormalDisconnection)]
    public void Each_end_keeps_its_disconnect_code(DisconnectReason reason, MqttDisconnectReasonCode expected)
    {
        Assert.Equal(expected, MqttReasonCodes.For(reason));
    }

    [Theory]
    [InlineData(AdmissionRefusal.Revoked, MqttConnectReasonCode.NotAuthorized)]
    [InlineData(AdmissionRefusal.TokenInvalid, MqttConnectReasonCode.BadUserNameOrPassword)]
    public void Each_refusal_keeps_its_connack_code(AdmissionRefusal refusal, MqttConnectReasonCode expected)
    {
        Assert.Equal(expected, MqttReasonCodes.For(refusal));
    }
}
