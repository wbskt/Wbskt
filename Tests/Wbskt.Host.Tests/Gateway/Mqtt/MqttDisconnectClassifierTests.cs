using System.Text;
using MQTTnet.Packets;
using MQTTnet.Server;
using Wbskt.Gateway.Core.Infrastructure;
using Wbskt.Gateway.MQTT.Host.Infrastructure;
using Wbskt.Protocol;

namespace Wbskt.Host.Tests.Gateway.Mqtt;

public sealed class MqttDisconnectClassifierTests
{
    private static readonly MqttUserProperty[] Updating = [new(MqttBinding.CloseReasonProperty, Encoding.UTF8.GetBytes(MqttBinding.UpdatingReason))];

    [Theory]
    [InlineData(MqttClientDisconnectType.Takeover, false, DisconnectReason.Superseded)]
    [InlineData(MqttClientDisconnectType.Clean, false, DisconnectReason.ClientClosed)]
    [InlineData(MqttClientDisconnectType.NotClean, false, DisconnectReason.Aborted)]
    [InlineData(MqttClientDisconnectType.NotClean, true, DisconnectReason.ServerStopping)]
    [InlineData(MqttClientDisconnectType.Clean, true, DisconnectReason.ClientClosed)]
    public void Each_end_maps_to_its_reason(MqttClientDisconnectType type, bool stopping, DisconnectReason expected)
    {
        Assert.Equal(expected, MqttDisconnectClassifier.Classify(type, null, packetTooBig: false, stopping));
    }

    [Fact]
    public void A_clean_disconnect_marked_updating_is_an_update()
    {
        Assert.Equal(DisconnectReason.Updating, MqttDisconnectClassifier.Classify(MqttClientDisconnectType.Clean, Updating, packetTooBig: false, stopping: false));
    }

    [Fact]
    public void An_abort_marked_updating_is_still_an_abort()
    {
        Assert.Equal(DisconnectReason.Aborted, MqttDisconnectClassifier.Classify(MqttClientDisconnectType.NotClean, Updating, packetTooBig: false, stopping: false));
    }

    [Fact]
    public void A_packet_over_the_limit_wins_over_how_the_socket_ended()
    {
        Assert.Equal(DisconnectReason.MessageTooBig, MqttDisconnectClassifier.Classify(MqttClientDisconnectType.NotClean, null, packetTooBig: true, stopping: true));
    }
}
