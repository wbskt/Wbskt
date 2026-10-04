using Microsoft.Extensions.Logging;
using Moq;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Workflow;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Security;
using Wbskt.Management.Host.Services.Workflow;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Abstraction.Validation;

namespace Wbskt.Workflow.Engine.Host.Tests.ManagementHost;

/// <summary>
/// Deleting a workflow and listing its versions. What matters is that a delete stops what is
/// running and evicts every version from the cache, that a workflow which is not this workspace's
/// reads as not found, and that only the newest version is ever reported live.
/// </summary>
public sealed class WorkflowDeletionTests
{
    private const int WorkspaceId = 3;
    private static readonly Guid RefId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    [Fact]
    public async Task Deleting_cancels_runs_in_flight_and_evicts_every_version()
    {
        var harness = new Harness();
        harness.Provider
            .Setup(p => p.DeleteAsync(RefId, WorkspaceId, 7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkflowDeletion([100L, 101L], [11, 12]));

        var result = await harness.Service.DeleteAsync(WorkspaceId, RefId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        harness.Runs.Verify(r => r.RequestCancellationAsync(100, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        harness.Runs.Verify(r => r.RequestCancellationAsync(101, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        harness.Cache.Verify(c => c.Invalidate(11), Times.Once);
        harness.Cache.Verify(c => c.Invalidate(12), Times.Once);
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
        harness.Runs.Verify(r => r.RequestCancellationAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Only_the_newest_version_is_reported_live()
    {
        var harness = new Harness();
        harness.Provider
            .Setup(p => p.GetVersionsAsync(RefId, WorkspaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([Version(3, enabled: false), Version(2, enabled: false), Version(1, enabled: true)]);

        var result = await harness.Service.GetVersionsAsync(WorkspaceId, RefId, CancellationToken.None);

        Assert.Equal([(3, "Deprecated"), (2, "Superseded"), (1, "Superseded")], result.Value.Select(v => (v.Version, v.Status)));
    }

    [Fact]
    public async Task Versions_of_a_workflow_that_is_not_here_are_not_found()
    {
        var harness = new Harness();
        harness.Provider
            .Setup(p => p.GetVersionsAsync(RefId, WorkspaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await harness.Service.GetVersionsAsync(WorkspaceId, RefId, CancellationToken.None);

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
            Service = new WorkflowDefinitionService(
                Provider.Object, Mock.Of<ITriggerRegistrationService>(), Cache.Object,
                new WorkflowValidator(), identity.Object, Runs.Object, Bus.Object, Mock.Of<ILogger<WorkflowDefinitionService>>());
        }

        public Mock<IWorkflowDefinitionProvider> Provider { get; } = new();

        public Mock<IWorkflowDefinitionCache> Cache { get; } = new();

        public Mock<IRunCancellationService> Runs { get; } = new();

        public Mock<IEventBus> Bus { get; } = new();

        public WorkflowDefinitionService Service { get; }
    }
}
