using System.Data;
using System.Text.Json;
using MassTransit;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;
using Wbskt.Events.Client;
using Wbskt.Management.Host.Handlers.Events;
using Wbskt.Management.Host.Providers;

namespace Wbskt.Host.Tests.ManagementHost;

/// <summary>Which table each event of a batch is written to: device traffic to its own, the rest to the audit log.</summary>
public sealed class EventLoggerHandlerTests
{
    private readonly Mock<IEventProvider> _provider = new();
    private readonly List<DataTable> _audit = [];
    private readonly List<DataTable> _traffic = [];

    public EventLoggerHandlerTests()
    {
        _provider.Setup(p => p.InsertBatchAsync(It.IsAny<DataTable>(), It.IsAny<CancellationToken>()))
            .Callback<DataTable, CancellationToken>((table, _) => _audit.Add(table))
            .Returns(Task.CompletedTask);
        _provider.Setup(p => p.InsertDeviceTrafficBatchAsync(It.IsAny<DataTable>(), It.IsAny<CancellationToken>()))
            .Callback<DataTable, CancellationToken>((table, _) => _traffic.Add(table))
            .Returns(Task.CompletedTask);
    }

    [Fact]
    public async Task Device_traffic_and_audit_events_in_one_batch_go_to_their_own_tables()
    {
        var clientRefId = Guid.NewGuid();
        await ConsumeAsync(
            new ClientConnectedEvent(clientRefId, 7, 3, "socket-1"),
            new ClientMessageReceivedEvent(clientRefId, 7, 3, "sensor", "{}"),
            new ClientMessageReceivedEvent(clientRefId, 7, 3, "sensor", "{}"));

        Assert.Equal(1, Assert.Single(_audit).Rows.Count);
        Assert.Equal(2, Assert.Single(_traffic).Rows.Count);
    }

    [Fact]
    public async Task A_batch_without_traffic_writes_nothing_to_the_traffic_table()
    {
        await ConsumeAsync(new ClientConnectedEvent(Guid.NewGuid(), 7, 3, "socket-1"));

        Assert.Single(_audit);
        _provider.Verify(p => p.InsertDeviceTrafficBatchAsync(It.IsAny<DataTable>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private async Task ConsumeAsync(params IEvent[] events)
    {
        var registry = new Mock<IEventRegistry>();
        registry.Setup(r => r.GetEventId(It.IsAny<string>())).Returns(1);
        var messages = events.Select(Message).ToList();
        var batch = new Mock<Batch<IEvent>>();
        batch.SetupGet(b => b.Length).Returns(messages.Count);
        batch.Setup(b => b.GetEnumerator()).Returns(() => messages.GetEnumerator());
        var context = new Mock<ConsumeContext<Batch<IEvent>>>();
        context.SetupGet(c => c.Message).Returns(batch.Object);
        context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);

        await new EventLoggerHandler(registry.Object, _provider.Object, NullLogger<EventLoggerHandler>.Instance).Consume(context.Object);
    }

    private static ConsumeContext<IEvent> Message(IEvent @event)
    {
        var body = new Mock<MessageBody>();
        body.Setup(b => b.GetBytes()).Returns(JsonSerializer.SerializeToUtf8Bytes(new { message = (object)@event }));
        var receive = new Mock<ReceiveContext>();
        receive.SetupGet(r => r.Body).Returns(body.Object);
        var context = new Mock<ConsumeContext<IEvent>>();
        context.SetupGet(c => c.Message).Returns(@event);
        context.SetupGet(c => c.MessageId).Returns(Guid.NewGuid());
        context.SetupGet(c => c.SupportedMessageTypes).Returns([$"urn:message:Wbskt.Events.Client:{@event.GetType().Name}"]);
        context.SetupGet(c => c.ReceiveContext).Returns(receive.Object);
        context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        return context.Object;
    }
}
