using MassTransit;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Auth;
using Wbskt.Events.Client;
using Wbskt.Events.Workflow;
using Wbskt.Infrastructure.Security;
using Wbskt.Management.Host.Handlers;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Providers;
using Wbskt.Management.Host.Services.Clients;

namespace Wbskt.Workflow.Engine.Host.Tests.ManagementHost;

public sealed class WorkspaceDeletedHandlerTests
{
    private const int WorkspaceId = 17;

    [Fact]
    public async Task Retires_the_workspace_and_acts_on_everything_it_reports()
    {
        var client = new RetiredClient(5, Guid.NewGuid(), 9, Guid.NewGuid());

        var provider = new Mock<IWorkspaceRetirementProvider>();
        provider.Setup(p => p.RetireAsync(WorkspaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkspaceRetirement([client], [101L, 102L], [31, 32]));
        var bus = new Mock<IEventBus>();
        var engine = new Mock<IWorkflowEngineGateway>();
        var cutoffs = new Mock<IClientTokenCutoffs>();

        var handler = new WorkspaceDeletedHandler(
            provider.Object, bus.Object, engine.Object, cutoffs.Object, NullLogger<WorkspaceDeletedHandler>.Instance);

        await handler.Consume(Context());

        // Socket hosts drop a client on any status other than Registered.
        bus.Verify(b => b.PublishAsync(
            It.Is<ClientStatusChangedEvent>(e =>
                e.ClientRefId == client.ClientRefId && e.ClientId == client.ClientId &&
                e.PolicyRefId == client.PolicyRefId && e.PolicyId == client.PolicyId &&
                e.WorkspaceId == WorkspaceId && e.Status == (byte)ClientStatus.Revoked),
            It.IsAny<CancellationToken>()), Times.Once);

        cutoffs.Verify(c => c.RevokeAsync(client.ClientRefId), Times.Once);

        // One cancel command per run still going, sent to the engine; nothing cancelled here.
        engine.Verify(e => e.CancelRunAsync(101L, "Workspace deleted.", It.IsAny<CancellationToken>()), Times.Once);
        engine.Verify(e => e.CancelRunAsync(102L, "Workspace deleted.", It.IsAny<CancellationToken>()), Times.Once);
        engine.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Sends_a_CancelWorkflowRun_command_per_active_run_on_the_real_bus()
    {
        // Wired through the real gateway: each active run becomes one CancelWorkflowRun on the bus the
        // engine reads, not a queued one and not a cancel run in this host.
        var provider = new Mock<IWorkspaceRetirementProvider>();
        provider.Setup(p => p.RetireAsync(WorkspaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkspaceRetirement([], [201L, 202L, 203L], []));
        var bus = new Mock<IEventBus>();
        var queued = new Mock<IEventBus>(MockBehavior.Strict);
        var gateway = new WorkflowEngineGateway(Mock.Of<IWorkflowEngineClient>(), bus.Object, queued.Object);

        var handler = new WorkspaceDeletedHandler(
            provider.Object, bus.Object, gateway, Mock.Of<IClientTokenCutoffs>(), NullLogger<WorkspaceDeletedHandler>.Instance);

        await handler.Consume(Context());

        foreach (long runId in new[] { 201L, 202L, 203L })
        {
            bus.Verify(b => b.PublishAsync(
                It.Is<CancelWorkflowRun>(c => c.RunId == runId && c.Reason == "Workspace deleted."),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        bus.Verify(b => b.PublishAsync(It.IsAny<CancelWorkflowRun>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task A_cancel_the_broker_refuses_fails_the_event_so_it_can_be_handled_again()
    {
        // Retiring is safe to repeat and still reports the run, so failing loudly is what gets the
        // command sent; swallowing it would leave the run going in a deleted workspace.
        var provider = new Mock<IWorkspaceRetirementProvider>();
        provider.Setup(p => p.RetireAsync(WorkspaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkspaceRetirement([], [301L], []));
        var engine = new Mock<IWorkflowEngineGateway>();
        engine.Setup(e => e.CancelRunAsync(301L, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("broker down"));

        var handler = new WorkspaceDeletedHandler(
            provider.Object, Mock.Of<IEventBus>(), engine.Object, Mock.Of<IClientTokenCutoffs>(), NullLogger<WorkspaceDeletedHandler>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Consume(Context()));
    }

    private static ConsumeContext<WorkspaceDeletedEvent> Context()
    {
        var ctx = new Mock<ConsumeContext<WorkspaceDeletedEvent>>();
        ctx.SetupGet(x => x.Message).Returns(new WorkspaceDeletedEvent(WorkspaceId, DeletedByUserId: 3));
        ctx.SetupGet(x => x.CancellationToken).Returns(CancellationToken.None);
        return ctx.Object;
    }
}
