using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Client;
using Wbskt.Infrastructure.Devices;
using Wbskt.Protocol;

namespace Wbskt.Host.Tests.Infrastructure;

/// <summary>
/// The console and workflows send commands through this one sender, so a device that cannot take a
/// command is refused the same way for both: unknown, invalid, offline or a broker that is down.
/// </summary>
public sealed class DeviceCommandSenderTests
{
    private const int WorkspaceId = 3;
    private const int ClientId = 7;
    private static readonly Guid ClientRefId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid CommandId = Guid.Parse("c0000000-0000-4000-8000-00000000000c");

    private readonly Mock<IClientCommandTargetProvider> _targets = new();
    private readonly Mock<IEventBus> _bus = new();

    private DeviceCommandSender Sender => new(_targets.Object, _bus.Object, NullLogger<DeviceCommandSender>.Instance);

    private static DeviceCommand Command(string type = "led.set", string payload = "{}", DateTime? expiresAtUtc = null) =>
        new(CommandId, ClientRefId, WorkspaceId, type, payload, expiresAtUtc);

    private void Target(ClientCommandTarget? target) =>
        _targets.Setup(t => t.FindAsync(ClientRefId, It.IsAny<CancellationToken>())).ReturnsAsync(target);

    [Fact]
    public async Task A_connected_device_gets_the_command_addressed_to_its_host_with_the_expiry()
    {
        Target(new ClientCommandTarget(ClientId, WorkspaceId, true, "socket-a"));
        var expiresAt = new DateTime(2026, 10, 12, 9, 0, 30, DateTimeKind.Utc);
        ClientCommandEvent? sent = null;
        _bus.Setup(b => b.PublishAsync(It.IsAny<ClientCommandEvent>(), It.IsAny<CancellationToken>()))
            .Callback<ClientCommandEvent, CancellationToken>((e, _) => sent = e)
            .Returns(Task.CompletedTask);

        var result = await Sender.SendAsync(Command(expiresAtUtc: expiresAt), e => e.ActorUserId = 99, CancellationToken.None);

        Assert.Equal(new DeviceCommandResult(DeviceCommandOutcome.Sent, ClientId), result);
        Assert.NotNull(sent);
        Assert.Equal("socket-a", sent!.TargetHostId);
        Assert.Equal(expiresAt, sent.ExpiresAtUtc);
        Assert.Equal(CommandId, sent.CommandId);
        Assert.Equal(99, sent.ActorUserId);
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(false, "socket-a")]
    [InlineData(true, null)]
    public async Task A_device_no_host_holds_is_offline(bool connected, string? hostId)
    {
        Target(new ClientCommandTarget(ClientId, WorkspaceId, connected, hostId));

        var result = await Sender.SendAsync(Command(), null, CancellationToken.None);

        Assert.Equal(DeviceCommandOutcome.Offline, result.Outcome);
        Assert.Equal(ClientId, result.ClientId);
        _bus.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_missing_device_and_one_of_another_workspace_read_the_same(bool otherWorkspace)
    {
        // A foreign device is not found before its presence is revealed.
        Target(otherWorkspace ? new ClientCommandTarget(ClientId, WorkspaceId + 1, false, null) : null);

        var result = await Sender.SendAsync(Command(), null, CancellationToken.None);

        Assert.Equal(new DeviceCommandResult(DeviceCommandOutcome.UnknownClient), result);
        _bus.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("SYS.ping", DeviceMessageRule.TypeReserved)]
    [InlineData("capabilities", DeviceMessageRule.TypeReserved)]
    [InlineData("", DeviceMessageRule.TypeLength)]
    public async Task A_command_breaking_the_protocol_rules_is_invalid(string type, DeviceMessageRule rule)
    {
        Target(new ClientCommandTarget(ClientId, WorkspaceId, true, "socket-a"));

        var result = await Sender.SendAsync(Command(type), null, CancellationToken.None);

        Assert.Equal(DeviceCommandOutcome.Invalid, result.Outcome);
        Assert.Equal(rule, result.Violation!.Rule);
        _bus.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_broker_that_fails_is_unavailable()
    {
        Target(new ClientCommandTarget(ClientId, WorkspaceId, true, "socket-a"));
        _bus.Setup(b => b.PublishAsync(It.IsAny<ClientCommandEvent>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("broker down"));

        var result = await Sender.SendAsync(Command(), null, CancellationToken.None);

        Assert.Equal(DeviceCommandOutcome.BrokerUnavailable, result.Outcome);
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
