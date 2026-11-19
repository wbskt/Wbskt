using Moq;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Client;
using Wbskt.Infrastructure.Clients;
using Wbskt.Models;
using Wbskt.Protocol;

namespace Wbskt.Host.Tests.Infrastructure;

/// <summary>
/// The console and workflows send commands through this one sender, so a client that cannot take a
/// command is refused the same way for both: unknown, invalid, inactive, a full queue or a broker that
/// is down. An offline, registered client has the command queued for it instead.
/// </summary>
public sealed class ClientCommandSenderTests
{
    private const int WorkspaceId = 3;
    private const int ClientId = 7;
    private static readonly Guid ClientRefId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid CommandId = Guid.Parse("c0000000-0000-4000-8000-00000000000c");

    private readonly Mock<IClientCommandTargetProvider> _targets = new();
    private readonly Mock<IEventBus> _bus = new();
    private readonly InMemoryQueuedCommandStore _queue = new();
    private readonly CommandQueueOptions _options = new();

    private ClientCommandSender Sender => TestClientCommands.Sender(_targets.Object, _bus.Object, _queue, options: _options);

    private static ClientCommand Command(string type = "led.set", string payload = "{}", DateTime? expiresAtUtc = null) =>
        new(CommandId, ClientRefId, WorkspaceId, type, payload, expiresAtUtc);

    private void Target(ClientCommandTarget? target) =>
        _targets.Setup(t => t.FindAsync(ClientRefId, It.IsAny<CancellationToken>())).ReturnsAsync(target);

    [Fact]
    public async Task A_connected_client_gets_the_command_addressed_to_its_host_with_the_expiry()
    {
        Target(new ClientCommandTarget(ClientId, WorkspaceId, true, "socket-a"));
        var expiresAt = DateTime.UtcNow.AddMinutes(30);
        ClientCommandEvent? sent = null;
        _bus.Setup(b => b.PublishAsync(It.IsAny<ClientCommandEvent>(), It.IsAny<CancellationToken>()))
            .Callback<ClientCommandEvent, CancellationToken>((e, _) => sent = e)
            .Returns(Task.CompletedTask);

        var result = await Sender.SendAsync(Command(expiresAtUtc: expiresAt), e => e.ActorUserId = 99, CancellationToken.None);

        Assert.Equal(new ClientCommandResult(ClientCommandOutcome.Sent, ClientId), result);
        Assert.NotNull(sent);
        Assert.Equal("socket-a", sent!.TargetHostId);
        Assert.Equal(expiresAt, sent.ExpiresAtUtc);
        Assert.Equal(CommandId, sent.CommandId);
        Assert.Equal(99, sent.ActorUserId);
    }

    [Theory]
    [InlineData(true, "socket-a")]
    [InlineData(false, null)]
    public async Task An_expiry_past_the_command_lifetime_is_refused_whoever_sends(bool connected, string? hostId)
    {
        Target(new ClientCommandTarget(ClientId, WorkspaceId, connected, hostId));

        var result = await Sender.SendAsync(Command(expiresAtUtc: DateTime.UtcNow + CommandLifetime.Max + TimeSpan.FromMinutes(5)), null, CancellationToken.None);

        Assert.Equal(ClientCommandOutcome.ExpiryInvalid, result.Outcome);
        Assert.Empty(_queue.Rows);
        _bus.Verify(b => b.PublishAsync(It.IsAny<ClientCommandEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(true, "socket-a")]
    [InlineData(false, null)]
    public async Task An_expiry_already_past_is_refused_rather_than_sent_to_be_dropped(bool connected, string? hostId)
    {
        Target(new ClientCommandTarget(ClientId, WorkspaceId, connected, hostId));

        var result = await Sender.SendAsync(Command(expiresAtUtc: DateTime.UtcNow - TimeSpan.FromMinutes(1)), null, CancellationToken.None);

        Assert.Equal(ClientCommandOutcome.ExpiryInvalid, result.Outcome);
        Assert.Empty(_queue.Rows);
        _bus.Verify(b => b.PublishAsync(It.IsAny<ClientCommandEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(false, "socket-a")]
    [InlineData(true, null)]
    public async Task A_client_no_host_holds_has_the_command_queued_with_its_sender(bool connected, string? hostId)
    {
        Target(new ClientCommandTarget(ClientId, WorkspaceId, connected, hostId));
        var expiresAt = DateTime.UtcNow.AddMinutes(30);
        ClientCommandQueuedEvent? announced = null;
        _bus.Setup(b => b.PublishAsync(It.IsAny<ClientCommandQueuedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<ClientCommandQueuedEvent, CancellationToken>((e, _) => announced = e)
            .Returns(Task.CompletedTask);

        var result = await Sender.SendAsync(Command(expiresAtUtc: expiresAt), e => e.ActorUserId = 99, CancellationToken.None);

        Assert.Equal(new ClientCommandResult(ClientCommandOutcome.Queued, ClientId, ExpiresAtUtc: expiresAt), result);
        var row = Assert.Single(_queue.Rows);
        Assert.Equal(CommandId, row.CommandId);
        Assert.Equal(expiresAt, row.ExpiresAtUtc);
        Assert.Equal(99, row.ActorUserId);
        Assert.NotNull(announced);
        Assert.Equal(CommandId, announced!.CommandId);
        Assert.Equal(99, announced.ActorUserId);
        _bus.Verify(b => b.PublishAsync(It.IsAny<ClientCommandEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(76)]
    [InlineData(null)]
    public async Task A_client_held_by_a_silent_host_has_the_command_queued(int? hostSilentSeconds)
    {
        // The host crashed: presence still names it, but nobody would deliver or hand back a command
        // published to it (#960).
        Target(new ClientCommandTarget(ClientId, WorkspaceId, true, "socket-a", HostSilentSeconds: hostSilentSeconds));

        var result = await Sender.SendAsync(Command(), null, CancellationToken.None);

        Assert.Equal(ClientCommandOutcome.Queued, result.Outcome);
        Assert.Single(_queue.Rows);
        _bus.Verify(b => b.PublishAsync(It.IsAny<ClientCommandEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_client_held_by_a_host_one_beat_late_still_gets_the_command_sent()
    {
        Target(new ClientCommandTarget(ClientId, WorkspaceId, true, "socket-a", HostSilentSeconds: 75));

        var result = await Sender.SendAsync(Command(), null, CancellationToken.None);

        Assert.Equal(ClientCommandOutcome.Sent, result.Outcome);
        Assert.Empty(_queue.Rows);
    }

    [Fact]
    public async Task A_command_sent_without_an_expiry_waits_the_default()
    {
        Target(new ClientCommandTarget(ClientId, WorkspaceId, false, null));
        _options.DefaultExpiryMinutes = 30;
        var before = DateTime.UtcNow;

        var result = await Sender.SendAsync(Command(), null, CancellationToken.None);

        Assert.Equal(ClientCommandOutcome.Queued, result.Outcome);
        Assert.InRange(result.ExpiresAtUtc!.Value, before.AddMinutes(30), DateTime.UtcNow.AddMinutes(30));
    }

    [Theory]
    [InlineData(ClientStatus.Pending)]
    [InlineData(ClientStatus.Revoked)]
    [InlineData(ClientStatus.Rejected)]
    public async Task An_offline_client_that_is_not_registered_is_inactive(ClientStatus status)
    {
        Target(new ClientCommandTarget(ClientId, WorkspaceId, false, null, status));

        var result = await Sender.SendAsync(Command(), null, CancellationToken.None);

        Assert.Equal(new ClientCommandResult(ClientCommandOutcome.Inactive, ClientId), result);
        Assert.Empty(_queue.Rows);
        _bus.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task An_offline_client_with_a_full_queue_is_refused()
    {
        Target(new ClientCommandTarget(ClientId, WorkspaceId, false, null));
        _options.MaxPerClient = 1;
        await _queue.EnqueueAsync(new QueuedCommand(Guid.NewGuid(), ClientId, ClientRefId, WorkspaceId, "led.set", "{}", DateTime.UtcNow.AddHours(1)), 1, CancellationToken.None);

        var result = await Sender.SendAsync(Command(), null, CancellationToken.None);

        Assert.Equal(new ClientCommandResult(ClientCommandOutcome.QueueFull, ClientId), result);
        Assert.Single(_queue.Rows);
        _bus.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_client_that_connects_while_the_command_is_queued_gets_it_at_once()
    {
        // Offline when the sender looks, connected by the time the row is in.
        _targets.SetupSequence(t => t.FindAsync(ClientRefId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClientCommandTarget(ClientId, WorkspaceId, false, null))
            .ReturnsAsync(new ClientCommandTarget(ClientId, WorkspaceId, true, "socket-b"));
        ClientCommandEvent? sent = null;
        _bus.Setup(b => b.PublishAsync(It.IsAny<ClientCommandEvent>(), It.IsAny<CancellationToken>()))
            .Callback<ClientCommandEvent, CancellationToken>((e, _) => sent = e)
            .Returns(Task.CompletedTask);

        var result = await Sender.SendAsync(Command(), e => e.ActorUserId = 99, CancellationToken.None);

        Assert.Equal(ClientCommandOutcome.Queued, result.Outcome);
        Assert.Empty(_queue.Rows);
        Assert.NotNull(sent);
        Assert.Equal("socket-b", sent!.TargetHostId);
        Assert.Equal(CommandId, sent.CommandId);
        Assert.Equal(99, sent.ActorUserId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_missing_client_and_one_of_another_workspace_read_the_same(bool otherWorkspace)
    {
        // A foreign client is not found before its presence is revealed.
        Target(otherWorkspace ? new ClientCommandTarget(ClientId, WorkspaceId + 1, false, null) : null);

        var result = await Sender.SendAsync(Command(), null, CancellationToken.None);

        Assert.Equal(new ClientCommandResult(ClientCommandOutcome.UnknownClient), result);
        _bus.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("SYS.ping", ClientMessageRule.TypeReserved)]
    [InlineData("capabilities", ClientMessageRule.TypeReserved)]
    [InlineData("", ClientMessageRule.TypeLength)]
    public async Task A_command_breaking_the_protocol_rules_is_invalid(string type, ClientMessageRule rule)
    {
        Target(new ClientCommandTarget(ClientId, WorkspaceId, true, "socket-a"));

        var result = await Sender.SendAsync(Command(type), null, CancellationToken.None);

        Assert.Equal(ClientCommandOutcome.Invalid, result.Outcome);
        Assert.Equal(rule, result.Violation!.Rule);
        _bus.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_broker_that_fails_is_unavailable()
    {
        Target(new ClientCommandTarget(ClientId, WorkspaceId, true, "socket-a"));
        _bus.Setup(b => b.PublishAsync(It.IsAny<ClientCommandEvent>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("broker down"));

        var result = await Sender.SendAsync(Command(), null, CancellationToken.None);

        Assert.Equal(ClientCommandOutcome.BrokerUnavailable, result.Outcome);
    }

    [Fact]
    public async Task A_caller_that_gives_up_is_cancelled_not_unavailable()
    {
        Target(new ClientCommandTarget(ClientId, WorkspaceId, true, "socket-a"));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        _bus.Setup(b => b.PublishAsync(It.IsAny<ClientCommandEvent>(), It.IsAny<CancellationToken>())).ThrowsAsync(new OperationCanceledException());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Sender.SendAsync(Command(), null, cancelled.Token));
    }
}
