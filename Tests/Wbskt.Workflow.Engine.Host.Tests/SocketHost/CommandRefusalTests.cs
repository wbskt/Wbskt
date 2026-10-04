using System.Text.Json;
using FluentAssertions;
using Wbskt.Socket.Host.Services;

namespace Wbskt.Workflow.Engine.Host.Tests.SocketHost;

public sealed class CommandRefusalTests
{
    private static JsonElement Payload(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void A_plain_ack_is_not_a_refusal()
    {
        CommandRefusal.TryRead(Payload("""{"commandId":"c"}"""), out _, out _).Should().BeFalse();
    }

    [Fact]
    public void A_refused_ack_carries_its_type_and_reason()
    {
        CommandRefusal.TryRead(Payload("""{"commandId":"c","type":"pump.start","refused":"expired"}"""), out var type, out var reason).Should().BeTrue();

        type.Should().Be("pump.start");
        reason.Should().Be("Refused by the device: expired");
    }

    [Fact]
    public void Device_supplied_strings_are_capped()
    {
        var json = JsonSerializer.Serialize(new { commandId = "c", type = new string('t', 500), refused = new string('r', 500) });

        CommandRefusal.TryRead(Payload(json), out var type, out var reason).Should().BeTrue();

        type.Should().HaveLength(100);
        reason.Should().HaveLength("Refused by the device: ".Length + 100);
    }

    [Theory]
    [InlineData("""{"commandId":"c","refused":""}""")]
    [InlineData("""{"commandId":"c","refused":5}""")]
    public void A_blank_or_non_string_refusal_is_read_as_a_plain_ack(string json)
    {
        CommandRefusal.TryRead(Payload(json), out _, out _).Should().BeFalse();
    }
}
