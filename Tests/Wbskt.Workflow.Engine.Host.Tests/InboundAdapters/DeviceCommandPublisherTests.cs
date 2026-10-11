using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;
using Wbskt.Events.Client;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Engine.Host.InboundAdapters;
using Wbskt.Workflow.Engine.Host.Providers;

namespace Wbskt.Workflow.Engine.Host.Tests.InboundAdapters;

public sealed class DeviceCommandPublisherTests
{
    private static readonly Guid ClientRefId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private const int ClientId = 7;
    private const int WorkspaceId = 3;
    private const string HostId = "socket-a";

    private readonly Mock<IEventBus> _bus = new();
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
        return new DeviceCommandPublisher(_bus.Object, targets.Object, NullLogger<DeviceCommandPublisher>.Instance);
    }

    [Fact]
    public async Task A_command_to_a_connected_device_is_addressed_to_the_host_holding_it()
    {
        await Publisher(new ClientCommandTarget(ClientId, WorkspaceId, true, HostId))
            .PublishCommandAsync(ClientRefId, WorkspaceId, "OpenVent", "{}", null, CancellationToken.None);

        Assert.NotNull(_sent);
        Assert.Equal(ClientRefId, _sent!.ClientRefId);
        Assert.Equal(ClientId, _sent.ClientId);
        Assert.Equal(WorkspaceId, _sent.WorkspaceId);
        Assert.Equal("OpenVent", _sent.Type);
        Assert.Equal("{}", _sent.Payload);
        Assert.Equal(HostId, _sent.TargetHostId);
        Assert.NotNull(_sent.CommandId);
        Assert.Null(_failed);
    }

    [Fact]
    public async Task A_command_to_an_offline_device_is_reported_as_failed_at_once()
    {
        await Publisher(new ClientCommandTarget(ClientId, WorkspaceId, false, null))
            .PublishCommandAsync(ClientRefId, WorkspaceId, "OpenVent", "{}", null, CancellationToken.None);

        Assert.Null(_sent);
        Assert.NotNull(_failed);
        Assert.Equal(ClientCommandFailedEvent.NotConnectedReason, _failed!.Reason);
        Assert.Equal(ClientId, _failed.ClientId);
        Assert.Equal("OpenVent", _failed.Type);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_command_to_a_device_that_is_missing_or_in_another_workspace_is_refused(bool otherWorkspace)
    {
        var target = otherWorkspace ? new ClientCommandTarget(ClientId, WorkspaceId + 1, true, HostId) : null;

        await Publisher(target).PublishCommandAsync(ClientRefId, WorkspaceId, "OpenVent", "{}", null, CancellationToken.None);

        Assert.Null(_sent);
        Assert.Equal(DeviceCommandPublisher.UnknownClientReason, _failed!.Reason);
        Assert.Equal(0, _failed.ClientId);
    }

    [Fact]
    public async Task A_command_a_run_sends_names_the_workflow_and_run_as_its_actor()
    {
        var sender = new CommandSender(Guid.NewGuid(), Guid.NewGuid());

        await Publisher(new ClientCommandTarget(ClientId, WorkspaceId, true, HostId))
            .PublishCommandAsync(ClientRefId, WorkspaceId, "OpenVent", "{}", sender, CancellationToken.None);

        Assert.Equal(EventSource.Workflow, _sent!.ActorSource);
        Assert.Equal(sender.WorkflowRefId, _sent.ActorWorkflowRefId);
        Assert.Equal(sender.RunRefId, _sent.ActorRunRefId);
        Assert.Null(_sent.ActorUserId);
    }
}
