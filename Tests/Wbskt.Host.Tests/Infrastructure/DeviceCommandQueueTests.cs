using Moq;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;
using Wbskt.Events.Client;
using Wbskt.Infrastructure.Devices;

namespace Wbskt.Host.Tests.Infrastructure;

/// <summary>
/// Commands waiting for an offline device: delivered oldest first on connect with their sender, put
/// back when the broker fails mid-delivery, and ended with a failure when they expire or the device
/// is revoked.
/// </summary>
public sealed class DeviceCommandQueueTests
{
    private const int WorkspaceId = 3;
    private const int ClientId = 7;
    private static readonly Guid ClientRefId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DateTime Now = new(2026, 10, 12, 9, 0, 0, DateTimeKind.Utc);

    private readonly Mock<IClientCommandTargetProvider> _targets = new();
    private readonly Mock<IEventBus> _bus = new();
    private readonly InMemoryQueuedCommandStore _store = new();
    private readonly List<IEvent> _published = [];

    public DeviceCommandQueueTests()
    {
        _bus.Setup(b => b.PublishAsync(It.IsAny<ClientCommandEvent>(), It.IsAny<CancellationToken>()))
            .Callback<ClientCommandEvent, CancellationToken>((e, _) => _published.Add(e))
            .Returns(Task.CompletedTask);
        _bus.Setup(b => b.PublishAsync(It.IsAny<ClientCommandFailedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<ClientCommandFailedEvent, CancellationToken>((e, _) => _published.Add(e))
            .Returns(Task.CompletedTask);
    }

    private DeviceCommandQueue Queue => TestDeviceCommands.Queue(_store, _targets.Object, _bus.Object, new FixedClock(Now));

    private async Task<QueuedCommand> WaitingAsync(string type, DateTime expiresAtUtc, int? actorUserId = null)
    {
        var command = new QueuedCommand(Guid.NewGuid(), ClientId, ClientRefId, WorkspaceId, type, "{}", expiresAtUtc, ActorUserId: actorUserId);
        await _store.EnqueueAsync(command, int.MaxValue, CancellationToken.None);
        return command;
    }

    [Fact]
    public async Task A_connect_delivers_the_unexpired_commands_oldest_first_with_their_sender()
    {
        var first = await WaitingAsync("first", Now.AddHours(1), actorUserId: 42);
        await WaitingAsync("stale", Now.AddMinutes(-1));
        var second = await WaitingAsync("second", Now.AddHours(2));

        int delivered = await Queue.DeliverAsync(ClientRefId, ClientId, WorkspaceId, "socket-a", CancellationToken.None);

        Assert.Equal(2, delivered);
        var sent = _published.Cast<ClientCommandEvent>().ToList();
        Assert.Equal([first.CommandId, second.CommandId], sent.Select(e => e.CommandId));
        Assert.All(sent, e => Assert.Equal("socket-a", e.TargetHostId));
        Assert.Equal(42, sent[0].ActorUserId);
        Assert.Equal(first.ExpiresAtUtc, sent[0].ExpiresAtUtc);
        Assert.Equal("stale", Assert.Single(_store.Rows).Type);
    }

    [Fact]
    public async Task A_broker_failure_mid_delivery_puts_the_unsent_commands_back()
    {
        await WaitingAsync("first", Now.AddHours(1));
        var second = await WaitingAsync("second", Now.AddHours(1));
        var third = await WaitingAsync("third", Now.AddHours(1));
        _bus.Setup(b => b.PublishAsync(It.Is<ClientCommandEvent>(e => e.CommandId == second.CommandId), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("broker down"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => Queue.DeliverAsync(ClientRefId, ClientId, WorkspaceId, "socket-a", CancellationToken.None));

        Assert.Equal([second.CommandId, third.CommandId], _store.Rows.Select(r => r.CommandId));
    }

    [Fact]
    public async Task Expired_commands_are_taken_and_end_as_failed()
    {
        var expired = await WaitingAsync("expired", Now.AddSeconds(-1));
        await WaitingAsync("waiting", Now.AddHours(1));

        int count = await Queue.ExpireAsync(10, CancellationToken.None);

        Assert.Equal(1, count);
        var failed = Assert.IsType<ClientCommandFailedEvent>(Assert.Single(_published));
        Assert.Equal(expired.CommandId, failed.CommandId);
        Assert.Equal(ClientCommandFailedEvent.ExpiredWhileQueuedReason, failed.Reason);
        Assert.Equal("waiting", Assert.Single(_store.Rows).Type);
    }

    [Fact]
    public async Task Dropping_a_device_ends_all_its_commands_even_if_one_announcement_fails()
    {
        var first = await WaitingAsync("first", Now.AddHours(1));
        var second = await WaitingAsync("second", Now.AddHours(1));
        _bus.Setup(b => b.PublishAsync(It.Is<ClientCommandFailedEvent>(e => e.CommandId == first.CommandId), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("broker down"));

        int count = await Queue.DropAsync(ClientId, ClientCommandFailedEvent.RevokedWhileQueuedReason, CancellationToken.None);

        Assert.Equal(2, count);
        Assert.Empty(_store.Rows);
        var failed = Assert.IsType<ClientCommandFailedEvent>(Assert.Single(_published));
        Assert.Equal(second.CommandId, failed.CommandId);
        Assert.Equal(ClientCommandFailedEvent.RevokedWhileQueuedReason, failed.Reason);
    }

    [Fact]
    public async Task A_command_queued_twice_by_id_waits_and_is_announced_once()
    {
        var command = new QueuedCommand(Guid.NewGuid(), ClientId, ClientRefId, WorkspaceId, "led.set", "{}", Now.AddHours(1));
        var announcement = new ClientCommandQueuedEvent(ClientRefId, ClientId, WorkspaceId, "led.set", "{}", command.CommandId, command.ExpiresAtUtc);

        Assert.True(await Queue.QueueAsync(command, announcement, 5, CancellationToken.None));
        Assert.True(await Queue.QueueAsync(command, announcement, 5, CancellationToken.None));

        Assert.Single(_store.Rows);
        _bus.Verify(b => b.PublishAsync(It.IsAny<ClientCommandQueuedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private sealed class FixedClock(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow);
    }
}
