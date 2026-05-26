using System.Text.Json;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Runtime;

namespace Wbskt.Workflow.Engine.Host.Tests.Runtime;

public sealed class CorrelationKeyResolverTests
{
    [Theory]
    [InlineData("device", "device:serial-1:telemetry", "deviceSerial", "serial-1", "payloadType", "telemetry")]
    [InlineData("schedule", "schedule:fire-1", "scheduledFireId", "fire-1", null, null)]
    [InlineData("webhook", "webhook:/hooks/intake", "webhookPath", "/hooks/intake", null, null)]
    [InlineData("manual", "manual:11111111-1111-1111-1111-111111111111", "workflowDefinitionRefId", "11111111-1111-1111-1111-111111111111", null, null)]
    [InlineData("signal", "signal:operator-ack:22222222-2222-2222-2222-222222222222", "signalName", "operator-ack", "scopeRunRefId", "22222222-2222-2222-2222-222222222222")]
    [InlineData("child-completed", "child-completed:33333333-3333-3333-3333-333333333333", "childRunRefId", "33333333-3333-3333-3333-333333333333", null, null)]
    [InlineData("http-wake", "http-wake:token-1", "wakeToken", "token-1", null, null)]
    public void Resolve_returns_expected_key_for_supported_channel(
        string channelKind,
        string expected,
        string field1,
        string value1,
        string? field2,
        string? value2)
    {
        // Arrange
        var resolver = new CorrelationKeyResolver();
        var payload = new Dictionary<string, JsonElement>
        {
            [field1] = JsonSerializer.SerializeToElement(value1)
        };

        if (field2 is not null && value2 is not null)
        {
            payload[field2] = JsonSerializer.SerializeToElement(value2);
        }

        var evt = new InboundEvent(channelKind, string.Empty, "evt-1", payload, DateTime.UtcNow);

        // Act
        string actual = resolver.Resolve(evt);

        // Assert
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Resolve_throws_when_required_payload_field_is_missing()
    {
        // Arrange
        var resolver = new CorrelationKeyResolver();
        var evt = new InboundEvent("device", string.Empty, "evt-1", new Dictionary<string, JsonElement>(), DateTime.UtcNow);

        // Act
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(new Action(() => resolver.Resolve(evt)));

        // Assert
        Assert.Contains("deviceSerial", ex.Message, StringComparison.Ordinal);
        Assert.Contains("device", ex.Message, StringComparison.Ordinal);
    }
}
