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
using Wbskt.Workflow.Abstraction.Validation;

namespace Wbskt.Workflow.Engine.Host.Tests.ManagementHost;

/// <summary>
/// Deleting a workflow and listing its versions. What matters is that a delete asks the engine to stop
/// what is running, that a workflow which is not this workspace's reads as not found, and that only
/// the newest version is ever reported live.
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
    public async Task Only_the_newest_version_is_reported_live()
    {
        var harness = new Harness();
        harness.Provider
            .Setup(p => p.GetVersionsAsync(RefId, WorkspaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([Version(3, enabled: false), Version(2, enabled: false), Version(1, enabled: true)]);

        var result = await harness.Queries.GetVersionsAsync(WorkspaceId, RefId, CancellationToken.None);

        Assert.Equal([(3, "Deprecated"), (2, "Superseded"), (1, "Superseded")], result.Value.Select(v => (v.Version, v.Status)));
    }

    [Fact]
    public async Task Versions_of_a_workflow_that_is_not_here_are_not_found()
    {
        var harness = new Harness();
        harness.Provider
            .Setup(p => p.GetVersionsAsync(RefId, WorkspaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await harness.Queries.GetVersionsAsync(WorkspaceId, RefId, CancellationToken.None);

        Assert.Equal("WORKFLOW_NOT_FOUND", result.Error.Code);
    }

    private static WorkflowVersionRow Version(int version, bool enabled) => new()
    {
        Version = version,
        Name = "wf",
        IsEnabled = enabled,
        PublishedBy = 7,
        CreatedAt = DateTime.UtcNow,
        RunCount = version * 10
    };

    private sealed class Harness
    {
        public Harness()
        {
            var identity = new Mock<IIdentityService>();
            identity.Setup(i => i.GetUserIdentity()).Returns(new UserIdentity(7));
            Service = new WorkflowLifecycleService(
                Provider.Object, Mock.Of<ITriggerRegistrationService>(),
                new WorkflowValidator(), identity.Object, Engine.Object, Bus.Object, Mock.Of<ILogger<WorkflowLifecycleService>>());
        }

        public Mock<IWorkflowDefinitionProvider> Provider { get; } = new();

        public Mock<IWorkflowEngineGateway> Engine { get; } = new();

        public Mock<IEventBus> Bus { get; } = new();

        public WorkflowLifecycleService Service { get; }

        public WorkflowQueryService Queries => new WorkflowQueryService(Provider.Object, new WorkflowValidator(), Mock.Of<ILogger<WorkflowQueryService>>());
    }
}
