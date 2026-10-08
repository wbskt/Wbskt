using FluentAssertions;
using Moq;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Client;
using Wbskt.Infrastructure.Security;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Services;

namespace Wbskt.Workflow.Engine.Host.Tests.ManagementHost;

/// <summary>
/// The rule the revoker exists for: the token cutoff is written before the event goes out, so a
/// revoked device is refused even when the broker is down or a socket host misses the event.
/// </summary>
public sealed class ClientAccessRevokerTests
{
    private const int WorkspaceId = 7;
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    private readonly List<string> _calls = [];
    private readonly Mock<IClientTokenCutoffs> _cutoffs = new();
    private readonly Mock<IEventBus> _bus = new();
    private readonly ClientAccessRevoker _revoker;

    public ClientAccessRevokerTests()
    {
        _cutoffs.Setup(c => c.RevokeAsync(It.IsAny<Guid>())).Callback(() => _calls.Add("revoke")).Returns(Task.CompletedTask);
        _cutoffs.Setup(c => c.RevokeIssuedBeforeAsync(It.IsAny<Guid>(), It.IsAny<DateTime>())).Callback(() => _calls.Add("revoke-before")).Returns(Task.CompletedTask);
        _cutoffs.Setup(c => c.ReinstateAsync(It.IsAny<Guid>(), It.IsAny<DateTime>())).Callback(() => _calls.Add("reinstate")).Returns(Task.CompletedTask);
        Record<ClientStatusChangedEvent>();
        Record<ClientDeletedEvent>();
        Record<ClientSecretRotatedEvent>();
        _revoker = new ClientAccessRevoker(_bus.Object, _cutoffs.Object, new FixedTime(Now));
    }

    [Fact]
    public async Task Leaving_registered_revokes_before_announcing()
    {
        var change = Change(ClientStatus.Registered);

        await _revoker.StatusChangedAsync(WorkspaceId, change, ClientStatus.Revoked, CancellationToken.None);

        _calls.Should().Equal("revoke", nameof(ClientStatusChangedEvent));
    }

    [Fact]
    public async Task Becoming_registered_reinstates_from_now_before_announcing()
    {
        var change = Change(ClientStatus.Pending);

        await _revoker.StatusChangedAsync(WorkspaceId, change, ClientStatus.Registered, CancellationToken.None);

        _calls.Should().Equal("reinstate", nameof(ClientStatusChangedEvent));
        _cutoffs.Verify(c => c.ReinstateAsync(change.RefId, Now.UtcDateTime), Times.Once);
    }

    [Fact]
    public async Task A_change_between_statuses_without_access_touches_no_cutoff()
    {
        await _revoker.StatusChangedAsync(WorkspaceId, Change(ClientStatus.Pending), ClientStatus.Rejected, CancellationToken.None);

        _calls.Should().Equal(nameof(ClientStatusChangedEvent));
    }

    [Fact]
    public async Task Deleting_revokes_before_announcing()
    {
        await _revoker.DeletedAsync(NewClient(), CancellationToken.None);

        _calls.Should().Equal("revoke", nameof(ClientDeletedEvent));
    }

    [Fact]
    public async Task Rotating_cuts_off_at_the_moment_the_event_carries()
    {
        var client = NewClient();

        await _revoker.SecretRotatedAsync(client, CancellationToken.None);

        _calls.Should().Equal("revoke-before", nameof(ClientSecretRotatedEvent));
        _cutoffs.Verify(c => c.RevokeIssuedBeforeAsync(client.RefId, Now.UtcDateTime), Times.Once);
        _bus.Verify(b => b.PublishAsync(
            It.Is<ClientSecretRotatedEvent>(e => e.RotatedAt == Now.UtcDateTime && e.WorkspaceId == WorkspaceId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Workspace_retirement_revokes_before_announcing_a_revoked_status()
    {
        var clientRef = Guid.NewGuid();

        await _revoker.RevokedAsync(WorkspaceId, clientRef, 5, Guid.NewGuid(), 9, CancellationToken.None);

        _calls.Should().Equal("revoke", nameof(ClientStatusChangedEvent));
        _bus.Verify(b => b.PublishAsync(
            It.Is<ClientStatusChangedEvent>(e => e.ClientRefId == clientRef && e.Status == (byte)ClientStatus.Revoked),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task The_cutoff_holds_when_the_broker_is_down()
    {
        _bus.Setup(b => b.PublishAsync(It.IsAny<ClientDeletedEvent>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("broker down"));

        var act = () => _revoker.DeletedAsync(NewClient(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _calls.Should().Equal("revoke");
    }

    private void Record<TEvent>() where TEvent : IEvent
    {
        _bus.Setup(b => b.PublishAsync(It.IsAny<TEvent>(), It.IsAny<CancellationToken>()))
            .Callback(() => _calls.Add(typeof(TEvent).Name))
            .Returns(Task.CompletedTask);
    }

    private static ClientStatusChange Change(ClientStatus oldStatus)
    {
        return new ClientStatusChange(Guid.NewGuid(), 5, 9, Guid.NewGuid(), oldStatus, ClientStatusOutcome.Updated);
    }

    private static Management.Host.Models.Client NewClient()
    {
        return new Management.Host.Models.Client { Id = 5, RefId = Guid.NewGuid(), WorkspaceId = WorkspaceId, PolicyId = 9, PolicyRefId = Guid.NewGuid(), Name = "greenhouse" };
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
