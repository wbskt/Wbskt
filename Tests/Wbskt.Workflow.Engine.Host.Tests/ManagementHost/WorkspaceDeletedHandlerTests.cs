using MassTransit;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Auth;
using Wbskt.Events.Client;
using Wbskt.Infrastructure.Security;
using Wbskt.Management.Host.Handlers;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Providers;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Engine.Host.Tests.ManagementHost;

public sealed class WorkspaceDeletedHandlerTests
{
    [Fact]
    public async Task Retires_the_workspace_and_acts_on_everything_it_reports()
    {
        const int workspaceId = 17;
        var client = new RetiredClient(5, Guid.NewGuid(), 9, Guid.NewGuid());

        var provider = new Mock<IWorkspaceRetirementProvider>();
        provider.Setup(p => p.RetireAsync(workspaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkspaceRetirement([client], [101L, 102L], [31, 32]));
        var bus = new Mock<IEventBus>();
        var cancellation = new Mock<IRunCancellationService>();
        var cache = new Mock<IWorkflowDefinitionCache>();
        var cutoffs = new Mock<IClientTokenCutoffs>();

        var handler = new WorkspaceDeletedHandler(
            provider.Object, bus.Object, cancellation.Object, cache.Object, cutoffs.Object, NullLogger<WorkspaceDeletedHandler>.Instance);

        var ctx = new Mock<ConsumeContext<WorkspaceDeletedEvent>>();
        ctx.SetupGet(x => x.Message).Returns(new WorkspaceDeletedEvent(workspaceId, DeletedByUserId: 3));
        ctx.SetupGet(x => x.CancellationToken).Returns(CancellationToken.None);

        await handler.Consume(ctx.Object);

        // Socket hosts drop a client on any status other than Registered.
        bus.Verify(b => b.PublishAsync(
            It.Is<ClientStatusChangedEvent>(e =>
                e.ClientRefId == client.ClientRefId && e.ClientId == client.ClientId &&
                e.PolicyRefId == client.PolicyRefId && e.PolicyId == client.PolicyId &&
                e.WorkspaceId == workspaceId && e.Status == (byte)ClientStatus.Revoked),
            It.IsAny<CancellationToken>()), Times.Once);

        cutoffs.Verify(c => c.RevokeAsync(client.ClientRefId), Times.Once);
        cancellation.Verify(c => c.RequestCancellationAsync(101L, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        cancellation.Verify(c => c.RequestCancellationAsync(102L, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        cache.Verify(c => c.Invalidate(31), Times.Once);
        cache.Verify(c => c.Invalidate(32), Times.Once);
    }
}
