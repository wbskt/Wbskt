using System.Text.Json;
using Moq;
using Wbskt.Management.Host.Controllers;
using Wbskt.Management.Host.Services;
using Wbskt.Management.Host.Services.Clients;
using Wbskt.Management.Models.Workflow;
using Wbskt.Primitives.Constants;
using Wbskt.Primitives.Exceptions;
using Wbskt.Primitives.Models;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Engine.Host.Tests.ManagementHost;

public sealed class WorkflowRunsControllerTests
{
    private const int WorkspaceId = 7;
    private static readonly Guid WorkspaceRef = Guid.Parse("99999999-9999-9999-9999-999999999999");

    private static Mock<IAuthServiceClient> AuthClientFor(PermissionSlug permission)
    {
        var authClient = new Mock<IAuthServiceClient>();
        authClient.Setup(x => x.ResolveWorkspaceAsync(WorkspaceRef, permission, It.IsAny<CancellationToken>()))
            .ReturnsAsync(WorkspaceId);
        return authClient;
    }

    [Fact]
    public async Task List_resolves_workspace_and_delegates_with_workspace_id()
    {
        var service = new Mock<IWorkflowRunQueryService>();
        var engineClient = new Mock<IWorkflowEngineClient>();
        var authClient = AuthClientFor(Permissions.WorkflowsRead);
        var workflowRefId = Guid.NewGuid();
        var expected = new RunListResponse([], null);
        service.Setup(x => x.ListByWorkflowAsync(WorkspaceId, workflowRefId, "Running", 25, 12, It.IsAny<CancellationToken>())).ReturnsAsync(expected);
        var controller = new WorkflowRunsController(service.Object, engineClient.Object, authClient.Object);

        var actual = await controller.List(WorkspaceRef, workflowRefId, "Running", 25, 12, CancellationToken.None);

        Assert.Equal(expected, actual);
        authClient.Verify(x => x.ResolveWorkspaceAsync(WorkspaceRef, Permissions.WorkflowsRead, It.IsAny<CancellationToken>()), Times.Once);
        service.Verify(x => x.ListByWorkflowAsync(WorkspaceId, workflowRefId, "Running", 25, 12, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Get_resolves_workspace_and_returns_detail_dto()
    {
        var service = new Mock<IWorkflowRunQueryService>();
        var engineClient = new Mock<IWorkflowEngineClient>();
        var authClient = AuthClientFor(Permissions.WorkflowsRead);
        var runRefId = Guid.NewGuid();
        var detail = new RunDetailDto(new RunSummaryDto(runRefId, Guid.NewGuid(), 3, "Running", "corr", DateTime.UtcNow, null), []);
        service.Setup(x => x.GetDetailAsync(WorkspaceId, runRefId, It.IsAny<CancellationToken>())).ReturnsAsync(detail);
        var controller = new WorkflowRunsController(service.Object, engineClient.Object, authClient.Object);

        var actual = await controller.Get(WorkspaceRef, runRefId, CancellationToken.None);

        Assert.Equal(detail, actual);
        service.Verify(x => x.GetDetailAsync(WorkspaceId, runRefId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Cancel_resolves_workspace_with_update_permission_and_delegates()
    {
        var service = new Mock<IWorkflowRunQueryService>();
        var engineClient = new Mock<IWorkflowEngineClient>();
        var authClient = AuthClientFor(Permissions.WorkflowsUpdate);
        var runRefId = Guid.NewGuid();
        var controller = new WorkflowRunsController(service.Object, engineClient.Object, authClient.Object);

        await controller.Cancel(WorkspaceRef, runRefId, new CancelRunRequest("operator request"), CancellationToken.None);

        authClient.Verify(x => x.ResolveWorkspaceAsync(WorkspaceRef, Permissions.WorkflowsUpdate, It.IsAny<CancellationToken>()), Times.Once);
        service.Verify(x => x.CancelAsync(WorkspaceId, runRefId, "operator request", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Signal_validates_run_ownership_then_delegates_to_engine_client()
    {
        var service = new Mock<IWorkflowRunQueryService>();
        var engineClient = new Mock<IWorkflowEngineClient>();
        var authClient = AuthClientFor(Permissions.WorkflowsUpdate);
        var runRefId = Guid.NewGuid();
        service.Setup(x => x.EnsureRunInWorkspaceAsync(WorkspaceId, runRefId, It.IsAny<CancellationToken>())).ReturnsAsync(55);
        var expected = new SignalResponse(true, "ResumedBookmark");
        engineClient.Setup(x => x.SignalAsync(runRefId, "wake", It.IsAny<SignalRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);
        var controller = new WorkflowRunsController(service.Object, engineClient.Object, authClient.Object);

        var response = await controller.Signal(WorkspaceRef, runRefId, "wake", new SignalRequest("wake", JsonSerializer.SerializeToElement(new { ready = true })), CancellationToken.None);

        Assert.True(response.Matched);
        Assert.Equal("ResumedBookmark", response.Outcome);
        service.Verify(x => x.EnsureRunInWorkspaceAsync(WorkspaceId, runRefId, It.IsAny<CancellationToken>()), Times.Once);
        engineClient.Verify(x => x.SignalAsync(runRefId, "wake", It.IsAny<SignalRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Service-level tests (WorkflowRunQueryService) ────────────────────────

    [Fact]
    public async Task ListByWorkflowAsync_validates_ownership_and_maps_runs()
    {
        var runProvider = new Mock<IRunProvider>();
        var branchProvider = new Mock<IBranchProvider>();
        var definitionProvider = new Mock<IWorkflowDefinitionProvider>();
        var cancellationService = new Mock<IRunCancellationService>();
        var workflowRefId = Guid.NewGuid();
        definitionProvider.Setup(x => x.GetCurrentByRefIdAsync(workflowRefId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateDefinitionRow(workflowRefId, WorkspaceId));
        runProvider.Setup(x => x.ListByWorkflowAsync(workflowRefId, "Running", 2, null, It.IsAny<CancellationToken>())).ReturnsAsync([
            CreateRunRow(41, Guid.NewGuid(), workflowRefId, 5, "Running"),
            CreateRunRow(42, Guid.NewGuid(), workflowRefId, 5, "Completed")
        ]);
        var service = new WorkflowRunQueryService(runProvider.Object, branchProvider.Object, definitionProvider.Object, cancellationService.Object);

        var response = await service.ListByWorkflowAsync(WorkspaceId, workflowRefId, "Running", 2, null, CancellationToken.None);

        Assert.Equal(2, response.Runs.Count);
        Assert.Equal(42, response.NextCursor);
        Assert.Equal(workflowRefId, response.Runs[0].WorkflowDefinitionRefId);
    }

    [Fact]
    public async Task ListByWorkflowAsync_throws_security_when_workflow_in_other_workspace()
    {
        var runProvider = new Mock<IRunProvider>();
        var branchProvider = new Mock<IBranchProvider>();
        var definitionProvider = new Mock<IWorkflowDefinitionProvider>();
        var cancellationService = new Mock<IRunCancellationService>();
        var workflowRefId = Guid.NewGuid();
        definitionProvider.Setup(x => x.GetCurrentByRefIdAsync(workflowRefId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateDefinitionRow(workflowRefId, WorkspaceId + 1));
        var service = new WorkflowRunQueryService(runProvider.Object, branchProvider.Object, definitionProvider.Object, cancellationService.Object);

        await Assert.ThrowsAsync<SecurityException>(() => service.ListByWorkflowAsync(WorkspaceId, workflowRefId, null, 10, null, CancellationToken.None));
    }

    [Fact]
    public async Task GetDetailAsync_validates_ownership_and_returns_run_with_branches()
    {
        var runProvider = new Mock<IRunProvider>();
        var branchProvider = new Mock<IBranchProvider>();
        var definitionProvider = new Mock<IWorkflowDefinitionProvider>();
        var cancellationService = new Mock<IRunCancellationService>();
        var runRefId = Guid.NewGuid();
        var workflowRefId = Guid.NewGuid();
        var run = CreateRunRow(51, runRefId, workflowRefId, 6, "Running");
        runProvider.Setup(x => x.GetByRefIdAsync(runRefId, It.IsAny<CancellationToken>())).ReturnsAsync(run);
        definitionProvider.Setup(x => x.GetByRefIdVersionAsync(workflowRefId, 6, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateDefinitionRow(workflowRefId, WorkspaceId));
        branchProvider.Setup(x => x.GetAllByRunIdAsync(51, It.IsAny<CancellationToken>())).ReturnsAsync([
            new BranchRow { Id = 100, RefId = Guid.NewGuid(), RunId = 51, ParentBranchId = null, ForkCohortId = null, NodeId = Guid.NewGuid(), Status = "Running", PendingTakePort = null, LocalJson = "{}", LastOutputJson = null, CompensationStackJson = null, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, RowVersion = [1] }
        ]);
        var service = new WorkflowRunQueryService(runProvider.Object, branchProvider.Object, definitionProvider.Object, cancellationService.Object);

        var detail = await service.GetDetailAsync(WorkspaceId, runRefId, CancellationToken.None);

        Assert.Equal(runRefId, detail.Summary.RefId);
        Assert.Single(detail.Branches);
    }

    [Fact]
    public async Task CancelAsync_validates_ownership_then_requests_cancellation()
    {
        var runProvider = new Mock<IRunProvider>();
        var branchProvider = new Mock<IBranchProvider>();
        var definitionProvider = new Mock<IWorkflowDefinitionProvider>();
        var cancellationService = new Mock<IRunCancellationService>();
        var runRefId = Guid.NewGuid();
        var workflowRefId = Guid.NewGuid();
        runProvider.Setup(x => x.GetByRefIdAsync(runRefId, It.IsAny<CancellationToken>())).ReturnsAsync(CreateRunRow(63, runRefId, workflowRefId, 2, "Running"));
        definitionProvider.Setup(x => x.GetByRefIdVersionAsync(workflowRefId, 2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateDefinitionRow(workflowRefId, WorkspaceId));
        var service = new WorkflowRunQueryService(runProvider.Object, branchProvider.Object, definitionProvider.Object, cancellationService.Object);

        await service.CancelAsync(WorkspaceId, runRefId, "missing", CancellationToken.None);

        cancellationService.Verify(x => x.RequestCancellationAsync(63, "missing", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EnsureRunInWorkspaceAsync_throws_security_when_run_in_other_workspace()
    {
        var runProvider = new Mock<IRunProvider>();
        var branchProvider = new Mock<IBranchProvider>();
        var definitionProvider = new Mock<IWorkflowDefinitionProvider>();
        var cancellationService = new Mock<IRunCancellationService>();
        var runRefId = Guid.NewGuid();
        var workflowRefId = Guid.NewGuid();
        runProvider.Setup(x => x.GetByRefIdAsync(runRefId, It.IsAny<CancellationToken>())).ReturnsAsync(CreateRunRow(70, runRefId, workflowRefId, 1, "Running"));
        definitionProvider.Setup(x => x.GetByRefIdVersionAsync(workflowRefId, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateDefinitionRow(workflowRefId, WorkspaceId + 99));
        var service = new WorkflowRunQueryService(runProvider.Object, branchProvider.Object, definitionProvider.Object, cancellationService.Object);

        await Assert.ThrowsAsync<SecurityException>(() => service.EnsureRunInWorkspaceAsync(WorkspaceId, runRefId, CancellationToken.None));
    }

    private static WorkflowDefinitionRow CreateDefinitionRow(Guid refId, int workspaceId)
    {
        return new WorkflowDefinitionRow
        {
            Id = 9,
            RefId = refId,
            Version = 1,
            WorkspaceId = workspaceId,
            Name = "wf",
            Description = null,
            IsEnabled = true,
            DefinitionJson = "{\"nodes\":[],\"edges\":[]}",
            PublishedBy = 1,
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };
    }

    private static RunRow CreateRunRow(int id, Guid refId, Guid workflowRefId, int version, string status)
    {
        return new RunRow
        {
            Id = id,
            RefId = refId,
            WorkflowDefinitionId = 9,
            WorkflowRefId = workflowRefId,
            WorkflowVersion = version,
            TriggerNodeId = Guid.NewGuid(),
            CorrelationKey = "corr",
            Status = status,
            StartedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            CompletedAt = status == "Completed" ? new DateTime(2026, 1, 1, 1, 0, 0, DateTimeKind.Utc) : null,
            CancellationRequestedAt = null,
            CancellationReason = null,
            CreditBudget = 10m,
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };
    }
}
