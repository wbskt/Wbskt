using System.Text.Json;
using MassTransit;
using Moq;
using Wbskt.Events.Client;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Models.Triggers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Engine.Host.InboundAdapters;

namespace Wbskt.Workflow.Engine.Host.Tests.InboundAdapters;

public sealed class ClientHoldRecorderTests
{
    private static readonly Guid ClientRef = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_client_message_is_recorded_against_each_hold_trigger_with_its_filter_result(bool filterPasses)
    {
        TriggerRegistrationRow typed = Registration(ClientHoldTriggerKey.Build(ClientRef, "temperature", 600, Guid.NewGuid(), Guid.NewGuid()));
        TriggerRegistrationRow anyType = Registration(ClientHoldTriggerKey.Build(ClientRef, "*", 30, Guid.NewGuid(), Guid.NewGuid()));
        var provider = new RecordingHoldStateProvider(typed, anyType);
        var consumer = new ClientPayloadReceivedConsumer(Hub().Object, new ClientHoldRecorder(provider, new FixedFilterEvaluator(filterPasses)));
        ClientMessageReceivedEvent evt = new(ClientRef, 12, 34, "temperature", "{\"celsius\":9.5}") { CreatedAtUtc = new DateTime(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc) };

        await consumer.Consume(Context(evt).Object);

        Assert.Equal([(ClientRef, "temperature", 34)], provider.Lookups);
        Assert.Equal(2, provider.Records.Count);
        Assert.Equal((typed.TriggerKey, filterPasses, evt.CreatedAtUtc, 600), (provider.Records[0].TriggerKey, provider.Records[0].Matches, provider.Records[0].EventAt, provider.Records[0].HoldSeconds));
        Assert.Equal((anyType.TriggerKey, filterPasses, 30), (provider.Records[1].TriggerKey, provider.Records[1].Matches, provider.Records[1].HoldSeconds));

        // The stored payload is the client trigger payload, so the run sees what a plain client trigger would.
        var stored = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(provider.Records[0].Payload)!;
        Assert.Equal("temperature", stored["messageType"].GetString());
        Assert.Equal(9.5, stored["payload"].GetProperty("celsius").GetDouble());
    }

    [Fact]
    public async Task A_buffered_message_is_recorded_at_the_time_the_device_sent_it()
    {
        // A reading sent offline must not look newer than readings that arrived before it,
        // or the hold would start from the time the device reconnected.
        TriggerRegistrationRow typed = Registration(ClientHoldTriggerKey.Build(ClientRef, "temperature", 600, Guid.NewGuid(), Guid.NewGuid()));
        var provider = new RecordingHoldStateProvider(typed);
        var consumer = new ClientPayloadReceivedConsumer(Hub().Object, new ClientHoldRecorder(provider, new FixedFilterEvaluator(true)));
        DateTime sentAt = new(2026, 10, 3, 11, 0, 0, DateTimeKind.Utc);
        ClientMessageReceivedEvent evt = new(ClientRef, 12, 34, "temperature", "{}")
        {
            CreatedAtUtc = new DateTime(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc),
            SentAtUtc = sentAt
        };

        await consumer.Consume(Context(evt).Object);

        Assert.Equal(sentAt, Assert.Single(provider.Records).EventAt);
    }

    [Fact]
    public async Task A_message_with_no_hold_triggers_records_nothing()
    {
        var provider = new RecordingHoldStateProvider();
        var consumer = new ClientPayloadReceivedConsumer(Hub().Object, new ClientHoldRecorder(provider, new FixedFilterEvaluator(true)));

        await consumer.Consume(Context(new ClientMessageReceivedEvent(ClientRef, 12, 34, "temperature", "{}")).Object);

        Assert.Single(provider.Lookups);
        Assert.Empty(provider.Records);
    }

    private static Mock<IInboundHub> Hub()
    {
        var hub = new Mock<IInboundHub>();
        hub.Setup(h => h.HandleAsync(It.IsAny<InboundEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TriggerDispatchResult(TriggerDispatchOutcome.NoRegistration, null, null, "ok"));
        return hub;
    }

    private static Mock<ConsumeContext<ClientMessageReceivedEvent>> Context(ClientMessageReceivedEvent evt)
    {
        var context = new Mock<ConsumeContext<ClientMessageReceivedEvent>>();
        context.SetupGet(c => c.Message).Returns(evt);
        context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        return context;
    }

    private static TriggerRegistrationRow Registration(string triggerKey) => new()
    {
        Id = 1,
        WorkflowDefinitionId = 1,
        WorkflowRefId = Guid.NewGuid(),
        WorkflowVersion = 1,
        TriggerNodeId = Guid.NewGuid(),
        TriggerKind = ClientHoldTriggerKey.TriggerKind,
        TriggerKey = triggerKey,
        CorrelationExpression = null,
        ConcurrencyPolicy = "Queue",
        FilterExpression = "{}",
        CreatedAt = DateTime.UtcNow
    };
}
