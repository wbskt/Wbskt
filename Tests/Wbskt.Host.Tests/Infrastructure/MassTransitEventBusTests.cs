using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Wbskt.EventBus.Abstractions;
using Wbskt.EventBus.RabbitMQ;

namespace Wbskt.Host.Tests.Infrastructure;

/// <summary>
/// The bus routes on an event's runtime type, so an event published through a base-typed variable
/// (a list of BaseEvent, the queue, the outbox) still reaches the consumers of its own type.
/// </summary>
public sealed class MassTransitEventBusTests
{
    [Fact]
    public async Task An_event_published_as_its_base_type_reaches_a_consumer_of_its_own_type()
    {
        await using var provider = new ServiceCollection()
            .AddMassTransitTestHarness(x => x.AddConsumer<ProbeConsumer>())
            .BuildServiceProvider(true);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        IEventBus bus = new MassTransitEventBus(harness.Bus);
        BaseEvent asBase = new ProbeEvent("hello");
        await bus.PublishAsync(asBase);
        IEvent asInterface = new ProbeEvent("again");
        await bus.PublishAsync(asInterface);

        var consumed = harness.GetConsumerHarness<ProbeConsumer>();
        Assert.True(await consumed.Consumed.Any<ProbeEvent>(m => m.Context.Message.Text == "hello"));
        Assert.True(await consumed.Consumed.Any<ProbeEvent>(m => m.Context.Message.Text == "again"));
    }

    public sealed record ProbeEvent(string Text) : BaseEvent;

    public sealed class ProbeConsumer : IConsumer<ProbeEvent>
    {
        public Task Consume(ConsumeContext<ProbeEvent> context) => Task.CompletedTask;
    }
}
