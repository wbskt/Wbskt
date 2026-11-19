using Wbskt.Management.Host.Services.Integrations;
using Wbskt.Management.Host.Providers;
using Microsoft.Extensions.Logging;
using Moq;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Workflow;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Security;
using Wbskt.Management.Host.Services.Clients;
using Wbskt.Management.Host.Services.Workflow;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Channels;
using Wbskt.Workflow.Runtime;
using Wbskt.Workflow.Abstraction.Validation;

namespace Wbskt.Host.Tests.ManagementHost;

/// <summary>
/// Deleting a workflow. What matters is that a delete asks the engine to stop what is running, and
/// that a workflow which is not this workspace's reads as not found.
/// </summary>
public sealed class WorkflowDeletionTests
{
    private const int WorkspaceId = 3;
    private static readonly Guid RefId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    [Fact]
    public async Task Deleting_queues_a_cancel_command_for_each_run_in_flight()
    {
        var harness = new Harness();
        harness.Provider
            .Setup(p => p.DeleteAsync(RefId, WorkspaceId, 7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkflowDeletion([100L, 101L], [11, 12]));

        var result = await harness.Service.DeleteAsync(WorkspaceId, RefId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        // Queued, not sent: the delete is already committed, so a broker outage must delay the cancels
        // rather than fail the request.
        harness.Engine.Verify(e => e.QueueCancelRunAsync(100, "Workflow deleted.", It.IsAny<CancellationToken>()), Times.Once);
        harness.Engine.Verify(e => e.QueueCancelRunAsync(101, "Workflow deleted.", It.IsAny<CancellationToken>()), Times.Once);
        harness.Engine.Verify(e => e.CancelRunAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        harness.Bus.Verify(b => b.PublishAsync(
            It.Is<WorkflowDeletedEvent>(e => e.WorkflowRefId == RefId && e.WorkflowId == 12 && e.WorkspaceId == WorkspaceId && e.CancelledRuns == 2),
            It.IsAny<CancellationToken>()), Times.Once);
        harness.Bus.Verify(b => b.PublishAsync(
            It.Is<WorkflowDefinitionChangedEvent>(e => e.WorkflowRefId == RefId && e.WorkspaceId == WorkspaceId && e.DefinitionIds.SequenceEqual(new[] { 11, 12 })),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Deleting_something_that_is_not_here_is_not_found()
    {
        var harness = new Harness();
        harness.Provider
            .Setup(p => p.DeleteAsync(RefId, WorkspaceId, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkflowDeletion?)null);

        var result = await harness.Service.DeleteAsync(WorkspaceId, RefId, CancellationToken.None);

        Assert.Equal("WORKFLOW_NOT_FOUND", result.Error.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        harness.Engine.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_delete_with_no_caller_is_unauthorized_and_deletes_nothing()
    {
        var harness = new Harness(signedIn: false);

        var result = await harness.Service.DeleteAsync(WorkspaceId, RefId, CancellationToken.None);

        Assert.Equal(ErrorType.Unauthorized, result.Error.Type);
        harness.Provider.VerifyNoOtherCalls();
    }

    private sealed class Harness
    {
        public Harness(bool signedIn = true)
        {
            var identity = new Mock<IIdentityService>();
            UserIdentity? caller = new UserIdentity(7);
            identity.Setup(i => i.TryGetUserIdentity(out caller)).Returns(signedIn);
            Service = new WorkflowLifecycleService(
                Provider.Object, Mock.Of<IWorkflowStateProvider>(), new TriggerPlanner(TriggerChannelRegistry.Default, TimeProvider.System),
                new WorkflowValidator(), new IntegrationReferenceCheck(Mock.Of<IIntegrationProvider>()), identity.Object, Engine.Object, Bus.Object, Mock.Of<ILogger<WorkflowLifecycleService>>());
        }

        public Mock<IWorkflowDefinitionProvider> Provider { get; } = new();

        public Mock<IWorkflowEngineGateway> Engine { get; } = new();

        public Mock<IEventBus> Bus { get; } = new();

        public WorkflowLifecycleService Service { get; }
    }
}
