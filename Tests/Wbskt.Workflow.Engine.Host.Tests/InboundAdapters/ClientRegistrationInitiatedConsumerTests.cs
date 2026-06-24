using System.Linq;
using MassTransit;
using Moq;
using Wbskt.Events.Management;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Engine.Host.InboundAdapters;

namespace Wbskt.Workflow.Engine.Host.Tests.InboundAdapters;

public sealed class ClientRegistrationInitiatedConsumerTests
{
    [Fact]
    public async Task Consume_invokes_inbound_hub_with_client_registered_channel()
    {
        var hub = new Mock<IInboundHub>();
        hub.Setup(h => h.HandleAsync(It.IsAny<InboundEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TriggerDispatchResult(TriggerDispatchOutcome.StartedRun, 1, null, "ok"));
        ClientRegistrationInitiatedEvent evt = new(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            12,
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            13,
            34,
            "client-1");
        var context = new Mock<ConsumeContext<ClientRegistrationInitiatedEvent>>();
        context.SetupGet(c => c.Message).Returns(evt);
        context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        var consumer = new ClientRegistrationInitiatedConsumer(hub.Object);

        await consumer.Consume(context.Object);

        hub.Verify(h => h.HandleAsync(
            It.Is<InboundEvent>(e =>
                e.ChannelKind == "client-registered"
                && e.MatchKeys.Contains($"client:{evt.ClientRefId}")
                && e.InboundEventId.StartsWith($"client-registered:{evt.ClientRefId}:", StringComparison.Ordinal)
                && e.Payload["clientRefId"].GetGuid() == evt.ClientRefId
                && e.Payload["policyRefId"].GetGuid() == evt.PolicyRefId
                && e.Payload["name"].GetString() == evt.Name),
            CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task Consume_uses_client_ref_id_as_correlation_key()
    {
        var hub = new Mock<IInboundHub>();
        hub.Setup(h => h.HandleAsync(It.IsAny<InboundEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TriggerDispatchResult(TriggerDispatchOutcome.StartedRun, 1, null, "ok"));
        ClientRegistrationInitiatedEvent evt = new(
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            12,
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            13,
            34,
            "client-2");
        var context = new Mock<ConsumeContext<ClientRegistrationInitiatedEvent>>();
        context.SetupGet(c => c.Message).Returns(evt);
        context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        var consumer = new ClientRegistrationInitiatedConsumer(hub.Object);

        await consumer.Consume(context.Object);

        hub.Verify(h => h.HandleAsync(
            It.Is<InboundEvent>(e => e.MatchKeys.Contains("client:bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb")),
            CancellationToken.None), Times.Once);
    }
}
