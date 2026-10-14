using System.Text.Json;
using Wbskt.Protocol;

namespace Wbskt.Host.Tests.ClientSdk;

/// <summary>
/// The device contract is decided in one place. These pin the decisions every host now shares: a
/// reserved type is reserved in any case, and the records the SDK and the management host exchange
/// keep their wire names.
/// </summary>
public sealed class ProtocolRulesTests
{
    [Theory]
    [InlineData("sys.ping")]
    [InlineData("SYS.ping")]
    [InlineData("Sys.Custom")]
    public void A_system_type_is_system_in_any_case(string type)
    {
        Assert.True(MessageTypes.IsSystem(type));
        Assert.True(MessageTypes.IsReserved(type));
    }

    [Theory]
    [InlineData("capabilities")]
    [InlineData("Capabilities")]
    [InlineData("STATE.REPORT")]
    public void The_types_the_server_reads_from_devices_cannot_be_sent_to_one(string type)
    {
        Assert.False(MessageTypes.IsSystem(type));
        Assert.NotNull(DeviceMessageRules.FindViolation(type, "{}"));
    }

    [Theory]
    [InlineData("led.set")]
    [InlineData("system.reboot")]
    public void An_ordinary_type_may_be_sent(string type)
    {
        Assert.Null(DeviceMessageRules.FindViolation(type, "{}"));
    }

    [Fact]
    public void A_payload_is_capped_at_half_a_socket_message()
    {
        Assert.Equal(SocketProtocol.MaxMessageBytes / 2, DeviceMessageRules.MaxPayloadChars);
        Assert.NotNull(DeviceMessageRules.FindViolation("led.set", new string('x', DeviceMessageRules.MaxPayloadChars + 1)));
    }

    [Fact]
    public void The_register_and_login_calls_keep_their_wire_names()
    {
        var web = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        Assert.Equal("""{"pin":"123456","name":"kitchen"}""", JsonSerializer.Serialize(new ClientRegistrationRequest("123456", "kitchen"), web));
        var clientRef = Guid.Parse("11111111-1111-1111-1111-111111111111");
        Assert.Equal($$"""{"clientRefId":"{{clientRef}}","secret":"s"}""", JsonSerializer.Serialize(new ClientLoginRequest(clientRef, "s"), web));
    }
}
