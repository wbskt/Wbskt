using System.Text.Json;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Workflow;
using Wbskt.Management.Host.Services.Clients;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Engine.Host.InboundAdapters;

namespace Wbskt.Workflow.Engine.Host.Tests.InboundAdapters;

public sealed class InboundCallbackConsumerTests
{
    private static readonly Guid WorkspaceRef = Guid.Parse("99999999-9999-9999-9999-999999999999");
    private static readonly TriggerDispatchResult NoRegistration = new(TriggerDispatchOutcome.NoRegistration, null, null, "ok");

    [Fact]
    public async Task A_webhook_reaches_the_hub_on_the_webhook_channel_with_its_secret_beside_the_payload()
    {
        // The secret must reach the dispatcher without ever entering the payload, which is persisted
        // as the run's trigger data and rendered in its history.
        var hub = RecordingHub(out List<InboundEvent> events);
        var messageId = Guid.NewGuid();

        await new InboundCallbackConsumer(hub.Object).Consume(Context(new InboundWebhook(WorkspaceRef, "alerts", "{\"value\":1}", "s3cret", null), messageId));

        InboundEvent e = Assert.Single(events);
        Assert.Equal("webhook", e.ChannelKind);
        Assert.Contains($"webhook:{WorkspaceRef}:alerts", e.MatchKeys);
        Assert.Equal($"webhook:{WorkspaceRef}:alerts:{messageId}", e.InboundEventId);
        Assert.Equal(1, e.Payload["body"].GetProperty("value").GetInt32());
        Assert.Equal("s3cret", e.Secret);
        Assert.False(e.Payload.ContainsKey("secret"));
    }

    [Fact]
    public async Task A_redelivered_webhook_without_a_key_is_the_same_event_and_another_call_is_not()
    {
        var hub = RecordingHub(out List<InboundEvent> events);
        var consumer = new InboundCallbackConsumer(hub.Object);
        var webhook = new InboundWebhook(WorkspaceRef, "alerts", "{}", null, null);
        var messageId = Guid.NewGuid();

        await consumer.Consume(Context(webhook, messageId));
        await consumer.Consume(Context(webhook, messageId));
        await consumer.Consume(Context(webhook, Guid.NewGuid()));

        Assert.Equal(events[0].InboundEventId, events[1].InboundEventId);
        Assert.NotEqual(events[0].InboundEventId, events[2].InboundEventId);
    }

    [Fact]
    public async Task The_same_idempotency_key_names_the_same_event_and_a_different_secret_does_not()
    {
        // The dispatcher dedupes on the event id, so a sender's retry (a new message) must produce the
        // same id. A caller without the secret must not be able to claim it ahead of the real sender.
        var hub = RecordingHub(out List<InboundEvent> events);
        var consumer = new InboundCallbackConsumer(hub.Object);

        Task SendAsync(string? key, string? secret, string path = "alerts") =>
            consumer.Consume(Context(new InboundWebhook(WorkspaceRef, path, "{}", secret, key), Guid.NewGuid()));

        await SendAsync("delivery-1", "s3cret");
        await SendAsync("delivery-1", "s3cret");
        await SendAsync("delivery-1", "guess");
        await SendAsync("delivery-1", "s3cret", path: "other");
        await SendAsync("delivery-2", "s3cret");
        await SendAsync(null, "s3cret");
        await SendAsync(null, "s3cret");

        List<string> ids = events.Select(e => e.InboundEventId).ToList();
        Assert.Equal(ids[0], ids[1]);
        Assert.Equal(7, ids.Count);
        Assert.Equal(6, ids.Distinct().Count());
        Assert.All(ids, id => Assert.True(id.Length + "inbound-event:".Length <= 200));
    }

    [Fact]
    public async Task A_wake_reaches_the_hub_on_the_http_wake_channel_named_by_its_message()
    {
        var hub = RecordingHub(out List<InboundEvent> events);
        var messageId = Guid.NewGuid();

        await new InboundCallbackConsumer(hub.Object).Consume(Context(new InboundWake("tok-1", "{\"approved\":true}"), messageId));

        InboundEvent e = Assert.Single(events);
        Assert.Equal("http-wake", e.ChannelKind);
        Assert.Contains("http-wake:tok-1", e.MatchKeys);
        Assert.Equal($"http-wake:tok-1:{messageId}", e.InboundEventId);
        Assert.Equal("tok-1", e.Payload["wakeToken"].GetString());
        Assert.True(e.Payload["body"].GetProperty("approved").GetBoolean());
    }

    [Fact]
    public async Task What_the_management_host_sends_lands_on_the_engines_callback_queue()
    {
        // The gateway sends to an address and the engine reads from a queue named by its consumer
        // definition: this is the test that they are the same queue, and that a webhook's body and
        // secret survive the trip.
        var received = new TaskCompletionSource<InboundEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        var hub = new Mock<IInboundHub>();
        hub.Setup(h => h.HandleAsync(It.IsAny<InboundEvent>(), It.IsAny<CancellationToken>()))
            .Callback<InboundEvent, CancellationToken>((e, _) => received.TrySetResult(e))
            .ReturnsAsync(NoRegistration);

        var services = new ServiceCollection();
        services.AddSingleton(hub.Object);
        services.AddMassTransit(x =>
        {
            // Registered the way AddRabbitMqEventBus registers the engine's consumers: an assembly scan,
            // which also picks up the definition.
            x.AddConsumers(typeof(InboundCallbackConsumer).Assembly);
            x.UsingInMemory((context, cfg) => cfg.ConfigureEndpoints(context));
        });
        await using ServiceProvider provider = services.BuildServiceProvider();
        var bus = provider.GetRequiredService<IBusControl>();
        await bus.StartAsync();
        try
        {
            var gateway = new WorkflowEngineGateway(Mock.Of<IWorkflowEngineClient>(), Mock.Of<IEventBus>(), Mock.Of<IEventBus>(), bus);

            await gateway.SendWebhookAsync(WorkspaceRef, "alerts", JsonDocument.Parse("{\"value\":1}").RootElement, "s3cret", null, CancellationToken.None);

            InboundEvent e = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Contains($"webhook:{WorkspaceRef}:alerts", e.MatchKeys);
            Assert.Equal(1, e.Payload["body"].GetProperty("value").GetInt32());
            Assert.Equal("s3cret", e.Secret);
        }
        finally
        {
            await bus.StopAsync();
        }
    }

    private static Mock<IInboundHub> RecordingHub(out List<InboundEvent> events)
    {
        var recorded = new List<InboundEvent>();
        var hub = new Mock<IInboundHub>();
        hub.Setup(h => h.HandleAsync(It.IsAny<InboundEvent>(), It.IsAny<CancellationToken>()))
            .Callback<InboundEvent, CancellationToken>((e, _) => recorded.Add(e))
            .ReturnsAsync(NoRegistration);
        events = recorded;
        return hub;
    }

    private static ConsumeContext<T> Context<T>(T message, Guid messageId) where T : class
    {
        var context = new Mock<ConsumeContext<T>>();
        context.SetupGet(c => c.Message).Returns(message);
        context.SetupGet(c => c.MessageId).Returns(messageId);
        context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        return context.Object;
    }
}
