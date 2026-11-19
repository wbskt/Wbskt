using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Auth;
using Wbskt.Infrastructure.Events.Outbox;

namespace Wbskt.Host.Tests.Infrastructure;

/// <summary>
/// The outbox is sent at least once and in order: a row leaves the table only after the broker took
/// it, and a row this host cannot read never holds up the ones behind it.
/// </summary>
public sealed class OutboxDispatcherTests
{
    private readonly FakeOutboxStore _store = new();
    private readonly Mock<IEventBus> _bus = new();

    private OutboxDispatcher CreateDispatcher() => new(_store, _bus.Object, NullLogger<OutboxDispatcher>.Instance);

    [Fact]
    public async Task A_queued_event_is_published_as_its_own_type_and_removed()
    {
        var deleted = new WorkspaceDeletedEvent(7, 42) { CreatedAtUtc = new DateTime(2026, 10, 12, 9, 0, 0, DateTimeKind.Utc) };
        _store.Add(OutboxMessage.Create(deleted));

        bool ok = await CreateDispatcher().DispatchPendingAsync(CancellationToken.None);

        Assert.True(ok);
        _bus.Verify(x => x.PublishAsync<IEvent>(deleted, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Empty(_store.Rows);
    }

    [Fact]
    public async Task A_publish_that_fails_leaves_the_row_and_everything_after_it()
    {
        _store.Add(OutboxMessage.Create(new WorkspaceDeletedEvent(1, 42)));
        _store.Add(OutboxMessage.Create(new WorkspaceDeletedEvent(2, 42)));
        _bus.Setup(x => x.PublishAsync<IEvent>(It.IsAny<WorkspaceDeletedEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("broker unavailable"));

        bool ok = await CreateDispatcher().DispatchPendingAsync(CancellationToken.None);

        Assert.False(ok);
        Assert.Equal(2, _store.Rows.Count);
        _bus.Verify(x => x.PublishAsync<IEvent>(It.IsAny<WorkspaceDeletedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_row_of_an_unknown_type_is_left_in_place_and_does_not_block_the_next()
    {
        _store.Add(new OutboxMessage("Wbskt.Events.Auth.SomethingFromANewerVersion", "{}"));
        _store.Add(OutboxMessage.Create(new WorkspaceDeletedEvent(3, 42)));
        var dispatcher = CreateDispatcher();

        Assert.True(await dispatcher.DispatchPendingAsync(CancellationToken.None));
        Assert.True(await dispatcher.DispatchPendingAsync(CancellationToken.None));

        _bus.Verify(x => x.PublishAsync<IEvent>(It.Is<WorkspaceDeletedEvent>(e => e.WorkspaceId == 3), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal("Wbskt.Events.Auth.SomethingFromANewerVersion", Assert.Single(_store.Rows).MessageType);
    }

    [Fact]
    public async Task A_row_that_cannot_be_read_is_left_in_place()
    {
        _store.Add(new OutboxMessage(typeof(WorkspaceDeletedEvent).FullName!, "not json"));

        Assert.True(await CreateDispatcher().DispatchPendingAsync(CancellationToken.None));

        Assert.Single(_store.Rows);
        _bus.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_full_batch_of_unreadable_rows_does_not_hold_up_the_rows_behind_it()
    {
        for (int i = 0; i < OutboxDispatcher.BatchSize + 5; i++)
        {
            _store.Add(new OutboxMessage("Wbskt.Events.Auth.SomethingFromANewerVersion", "{}"));
        }

        _store.Add(OutboxMessage.Create(new WorkspaceDeletedEvent(9, 42)));
        var dispatcher = CreateDispatcher();

        Assert.True(await dispatcher.DispatchPendingAsync(CancellationToken.None));

        _bus.Verify(x => x.PublishAsync<IEvent>(It.Is<WorkspaceDeletedEvent>(e => e.WorkspaceId == 9), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(OutboxDispatcher.BatchSize + 5, _store.Rows.Count);
        Assert.Equal(OutboxDispatcher.BatchSize + 5, dispatcher.UnreadableRows);
    }

    [Fact]
    public async Task The_unreadable_count_falls_once_the_rows_are_removed()
    {
        _store.Add(new OutboxMessage(typeof(WorkspaceDeletedEvent).FullName!, "not json"));
        var dispatcher = CreateDispatcher();
        Assert.True(await dispatcher.DispatchPendingAsync(CancellationToken.None));
        Assert.Equal(1, dispatcher.UnreadableRows);

        _store.Rows.Clear();
        Assert.True(await dispatcher.DispatchPendingAsync(CancellationToken.None));

        Assert.Equal(0, dispatcher.UnreadableRows);
    }

    [Fact]
    public async Task More_than_a_batch_is_sent_in_one_pass()
    {
        for (int i = 0; i < OutboxDispatcher.BatchSize + 3; i++)
        {
            _store.Add(OutboxMessage.Create(new WorkspaceDeletedEvent(i, 42)));
        }

        Assert.True(await CreateDispatcher().DispatchPendingAsync(CancellationToken.None));

        Assert.Empty(_store.Rows);
        _bus.Verify(x => x.PublishAsync<IEvent>(It.IsAny<WorkspaceDeletedEvent>(), It.IsAny<CancellationToken>()), Times.Exactly(OutboxDispatcher.BatchSize + 3));
    }

    [Fact]
    public async Task Events_are_sent_oldest_first()
    {
        var sent = new List<int>();
        _bus.Setup(x => x.PublishAsync<IEvent>(It.IsAny<WorkspaceDeletedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<IEvent, CancellationToken>((e, _) => sent.Add(((WorkspaceDeletedEvent)e).WorkspaceId))
            .Returns(Task.CompletedTask);
        _store.Add(OutboxMessage.Create(new WorkspaceDeletedEvent(1, 42)));
        _store.Add(OutboxMessage.Create(new WorkspaceDeletedEvent(2, 42)));

        await CreateDispatcher().DispatchPendingAsync(CancellationToken.None);

        Assert.Equal([1, 2], sent);
    }

    private sealed class FakeOutboxStore : IOutboxStore
    {
        private long _nextId = 1;

        public List<PendingOutboxMessage> Rows { get; } = [];

        public void Add(OutboxMessage message) => Rows.Add(new PendingOutboxMessage(_nextId++, message.MessageType, message.Payload, DateTime.UtcNow));

        public Task<IReadOnlyCollection<PendingOutboxMessage>> GetPendingAsync(int take, long? afterId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<PendingOutboxMessage>>(Rows.Where(r => afterId is null || r.Id > afterId).OrderBy(r => r.Id).Take(take).ToList());

        public Task DeleteAsync(long id, CancellationToken cancellationToken)
        {
            Rows.RemoveAll(r => r.Id == id);
            return Task.CompletedTask;
        }
    }
}
