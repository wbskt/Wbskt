using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;
using Wbskt.Events.Client;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Engine.Host.InboundAdapters;
using Wbskt.Infrastructure.Devices;

namespace Wbskt.Workflow.Engine.Host.Tests.InboundAdapters;

public sealed class DeviceCommandPublisherTests
{
    private static readonly Guid ClientRefId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid CommandId = Guid.Parse("c0000000-0000-4000-8000-00000000000c");
    private const int ClientId = 7;
    private const int WorkspaceId = 3;
    private const string HostId = "socket-a";

    private static readonly TimeSpan ExpiresIn = TimeSpan.FromSeconds(30);

    private readonly Mock<IEventBus> _bus = new();
    private readonly FixedClock _clock = new(new DateTimeOffset(2026, 10, 12, 9, 0, 0, TimeSpan.Zero));
    private ClientCommandEvent? _sent;
    private ClientCommandFailedEvent? _failed;

    public DeviceCommandPublisherTests()
    {
        _bus.Setup(b => b.PublishAsync(It.IsAny<ClientCommandEvent>(), It.IsAny<CancellationToken>()))
            .Callback<ClientCommandEvent, CancellationToken>((e, _) => _sent = e)
            .Returns(Task.CompletedTask);
        _bus.Setup(b => b.PublishAsync(It.IsAny<ClientCommandFailedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<ClientCommandFailedEvent, CancellationToken>((e, _) => _failed = e)
            .Returns(Task.CompletedTask);
    }

    private DeviceCommandPublisher Publisher(ClientCommandTarget? target)
    {
        var targets = new Mock<IClientCommandTargetProvider>();
        targets.Setup(t => t.FindAsync(ClientRefId, It.IsAny<CancellationToken>())).ReturnsAsync(target);
        var sender = new DeviceCommandSender(targets.Object, _bus.Object, NullLogger<DeviceCommandSender>.Instance);
        return new DeviceCommandPublisher(sender, _bus.Object, _clock);
    }

    [Fact]
    public async Task A_command_to_a_connected_device_is_addressed_to_the_host_holding_it()
    {
        await Publisher(new ClientCommandTarget(ClientId, WorkspaceId, true, HostId))
            .PublishCommandAsync(CommandId, ClientRefId, WorkspaceId, "OpenVent", "{}", ExpiresIn, null, CancellationToken.None);

        Assert.NotNull(_sent);
        Assert.Equal(ClientRefId, _sent!.ClientRefId);
        Assert.Equal(ClientId, _sent.ClientId);
        Assert.Equal(WorkspaceId, _sent.WorkspaceId);
        Assert.Equal("OpenVent", _sent.Type);
        Assert.Equal("{}", _sent.Payload);
        Assert.Equal(HostId, _sent.TargetHostId);
        Assert.Equal(CommandId, _sent.CommandId);
        Assert.Equal(_clock.GetUtcNow().UtcDateTime + ExpiresIn, _sent.ExpiresAtUtc);
        Assert.Null(_failed);
    }

    [Fact]
    public async Task A_broker_that_is_down_fails_the_step_instead_of_reporting_a_device_failure()
    {
        _bus.Setup(b => b.PublishAsync(It.IsAny<ClientCommandEvent>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("broker down"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => Publisher(new ClientCommandTarget(ClientId, WorkspaceId, true, HostId))
            .PublishCommandAsync(CommandId, ClientRefId, WorkspaceId, "OpenVent", "{}", ExpiresIn, null, CancellationToken.None));

        Assert.Null(_failed);
    }

    [Fact]
    public async Task A_command_to_an_offline_device_is_reported_as_failed_at_once()
    {
        await Publisher(new ClientCommandTarget(ClientId, WorkspaceId, false, null))
            .PublishCommandAsync(CommandId, ClientRefId, WorkspaceId, "OpenVent", "{}", ExpiresIn, null, CancellationToken.None);

        Assert.Null(_sent);
        Assert.NotNull(_failed);
        Assert.Equal(ClientCommandFailedEvent.NotConnectedReason, _failed!.Reason);
        // The step waiting on this command is woken by the failure, so it carries the command's id.
        Assert.Equal(CommandId, _failed.CommandId);
        Assert.Equal(ClientId, _failed.ClientId);
        Assert.Equal("OpenVent", _failed.Type);
    }

    [Theory]
    [InlineData("sys.ping")]
    [InlineData("state.report")]
    public async Task A_reserved_command_type_is_refused_and_reported(string type)
    {
        await Publisher(new ClientCommandTarget(ClientId, WorkspaceId, true, HostId))
            .PublishCommandAsync(CommandId, ClientRefId, WorkspaceId, type, "{}", ExpiresIn, null, CancellationToken.None);

        Assert.Null(_sent);
        Assert.NotNull(_failed);
        Assert.Contains("reserved", _failed!.Reason);
        Assert.Equal(ClientId, _failed.ClientId);
    }

    [Fact]
    public async Task An_oversized_payload_is_refused_and_reported()
    {
        string payload = new('x', 32 * 1024 + 1);

        await Publisher(new ClientCommandTarget(ClientId, WorkspaceId, true, HostId))
            .PublishCommandAsync(CommandId, ClientRefId, WorkspaceId, "OpenVent", payload, ExpiresIn, null, CancellationToken.None);

        Assert.Null(_sent);
        Assert.NotNull(_failed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_command_to_a_device_that_is_missing_or_in_another_workspace_is_refused(bool otherWorkspace)
    {
        var target = otherWorkspace ? new ClientCommandTarget(ClientId, WorkspaceId + 1, true, HostId) : null;

        await Publisher(target).PublishCommandAsync(CommandId, ClientRefId, WorkspaceId, "OpenVent", "{}", ExpiresIn, null, CancellationToken.None);

        Assert.Null(_sent);
        Assert.Equal(DeviceCommandPublisher.UnknownClientReason, _failed!.Reason);
        Assert.Equal(0, _failed.ClientId);
    }

    [Fact]
    public async Task A_command_a_run_sends_names_the_workflow_and_run_as_its_actor()
    {
        var sender = new CommandSender(Guid.NewGuid(), Guid.NewGuid());

        await Publisher(new ClientCommandTarget(ClientId, WorkspaceId, true, HostId))
            .PublishCommandAsync(CommandId, ClientRefId, WorkspaceId, "OpenVent", "{}", ExpiresIn, sender, CancellationToken.None);

        Assert.Equal(EventSource.Workflow, _sent!.ActorSource);
        Assert.Equal(sender.WorkflowRefId, _sent.ActorWorkflowRefId);
        Assert.Equal(sender.RunRefId, _sent.ActorRunRefId);
        Assert.Null(_sent.ActorUserId);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
