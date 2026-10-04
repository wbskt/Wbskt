using System.Text.Json;
using FluentAssertions;
using Wbskt.Client.Sdk.Internal;

namespace Wbskt.Workflow.Engine.Host.Tests.ClientSdk;

public sealed class CommandExpiryTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

    private static JsonElement Frame(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void A_command_past_its_expiry_is_expired()
    {
        CommandExpiry.IsExpired(Frame("""{"type":"x","commandId":"c","expiresAt":"2026-10-04T11:59:59Z"}"""), Now).Should().BeTrue();
    }

    [Fact]
    public void A_command_before_its_expiry_is_not()
    {
        CommandExpiry.IsExpired(Frame("""{"type":"x","commandId":"c","expiresAt":"2026-10-04T12:00:30Z"}"""), Now).Should().BeFalse();
    }

    [Theory]
    [InlineData("""{"type":"x","commandId":"c"}""")]
    [InlineData("""{"type":"x","commandId":"c","expiresAt":"not a date"}""")]
    [InlineData("""{"type":"x","commandId":"c","expiresAt":null}""")]
    public void A_missing_or_unreadable_expiry_never_drops_a_command(string json)
    {
        CommandExpiry.IsExpired(Frame(json), Now).Should().BeFalse();
    }
}
