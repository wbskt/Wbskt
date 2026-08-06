using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Wbskt.Infrastructure;
using Wbskt.Management.Host.Controllers.Workflow;
using Wbskt.Management.Host.Services.Clients;
using Wbskt.Management.Host.Services.Workflow;
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
            .ReturnsAsync(Result<int>.Success(WorkspaceId));
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
        service.Setup(x => x.ListByWorkflowAsync(WorkspaceId, workflowRefId, "Running", 25, 12, It.IsAny<CancellationToken>())).ReturnsAsync(Result<RunListResponse>.Success(expected));
        var controller = new WorkflowRunsController(service.Object, engineClient.Object, authClient.Object, Mock.Of<ILogger<WorkflowRunsController>>());

        var actual = await controller.List(WorkspaceRef, workflowRefId, "Running", 25, 12, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actual.Result);
        Assert.Equal(expected, okResult.Value);
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
        service.Setup(x => x.GetDetailAsync(WorkspaceId, runRefId, It.IsAny<CancellationToken>())).ReturnsAsync(Result<RunDetailDto>.Success(detail));
        var controller = new WorkflowRunsController(service.Object, engineClient.Object, authClient.Object, Mock.Of<ILogger<WorkflowRunsController>>());

        var actual = await controller.Get(WorkspaceRef, runRefId, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actual.Result);
        Assert.Equal(detail, okResult.Value);
        service.Verify(x => x.GetDetailAsync(WorkspaceId, runRefId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Cancel_resolves_workspace_with_update_permission_and_delegates()
    {
        var service = new Mock<IWorkflowRunQueryService>();
        var engineClient = new Mock<IWorkflowEngineClient>();
        var authClient = AuthClientFor(Permissions.WorkflowsExecute);
        var runRefId = Guid.NewGuid();
        service.Setup(x => x.CancelAsync(WorkspaceId, runRefId, "operator request", It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success());
        var controller = new WorkflowRunsController(service.Object, engineClient.Object, authClient.Object, Mock.Of<ILogger<WorkflowRunsController>>());

        var response = await controller.Cancel(WorkspaceRef, runRefId, new CancelRunRequest("operator request"), CancellationToken.None);

        Assert.IsType<NoContentResult>(response);
        authClient.Verify(x => x.ResolveWorkspaceAsync(WorkspaceRef, Permissions.WorkflowsExecute, It.IsAny<CancellationToken>()), Times.Once);
        service.Verify(x => x.CancelAsync(WorkspaceId, runRefId, "operator request", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Signal_validates_run_ownership_then_delegates_to_engine_client()
    {
        var service = new Mock<IWorkflowRunQueryService>();
        var engineClient = new Mock<IWorkflowEngineClient>();
        var authClient = AuthClientFor(Permissions.WorkflowsExecute);
        var runRefId = Guid.NewGuid();
        service.Setup(x => x.EnsureRunInWorkspaceAsync(WorkspaceId, runRefId, It.IsAny<CancellationToken>())).ReturnsAsync(Result<int>.Success(55));
        var expected = new SignalResponse(true, "ResumedBookmark");
        engineClient.Setup(x => x.SignalAsync(runRefId, "wake", It.IsAny<SignalRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);
        var controller = new WorkflowRunsController(service.Object, engineClient.Object, authClient.Object, Mock.Of<ILogger<WorkflowRunsController>>());

        var response = await controller.Signal(WorkspaceRef, runRefId, "wake", new SignalRequest("wake", JsonSerializer.SerializeToElement(new { ready = true })), CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(response.Result);
        var signalResponse = Assert.IsType<SignalResponse>(okResult.Value);
        Assert.True(signalResponse.Matched);
        Assert.Equal("ResumedBookmark", signalResponse.Outcome);
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
        // The service asks for one more than the page size to detect a further page; three rows come
        // back, so the page is trimmed to two and a cursor is returned.
        runProvider.Setup(x => x.ListByWorkflowAsync(workflowRefId, "Running", 3, null, It.IsAny<CancellationToken>())).ReturnsAsync([
            CreateRunRow(41, Guid.NewGuid(), workflowRefId, 5, "Running"),
            CreateRunRow(42, Guid.NewGuid(), workflowRefId, 5, "Completed"),
            CreateRunRow(43, Guid.NewGuid(), workflowRefId, 5, "Completed")
        ]);
        var service = new WorkflowRunQueryService(runProvider.Object, branchProvider.Object, definitionProvider.Object, cancellationService.Object, Mock.Of<ILogger<WorkflowRunQueryService>>());

        var response = await service.ListByWorkflowAsync(WorkspaceId, workflowRefId, "Running", 2, null, CancellationToken.None);

        Assert.True(response.IsSuccess);
        Assert.Equal(2, response.Value.Runs.Count);
        Assert.Equal(42, response.Value.NextCursor);
        Assert.Equal(workflowRefId, response.Value.Runs[0].WorkflowDefinitionRefId);
    }

    [Fact]
    public async Task ListByWorkflowAsync_returns_no_cursor_when_the_page_lands_exactly_on_the_end()
    {
        // Regression: "a full page means there is more" handed back a cursor even when the results
        // ended exactly on the page boundary, so clients always fetched one empty page.
        var runProvider = new Mock<IRunProvider>();
        var definitionProvider = new Mock<IWorkflowDefinitionProvider>();
        var workflowRefId = Guid.NewGuid();
        definitionProvider.Setup(x => x.GetCurrentByRefIdAsync(workflowRefId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateDefinitionRow(workflowRefId, WorkspaceId));
        runProvider.Setup(x => x.ListByWorkflowAsync(workflowRefId, null, 3, null, It.IsAny<CancellationToken>())).ReturnsAsync([
            CreateRunRow(41, Guid.NewGuid(), workflowRefId, 5, "Running"),
            CreateRunRow(42, Guid.NewGuid(), workflowRefId, 5, "Completed")
        ]);
        var service = new WorkflowRunQueryService(runProvider.Object, Mock.Of<IBranchProvider>(), definitionProvider.Object, Mock.Of<IRunCancellationService>(), Mock.Of<ILogger<WorkflowRunQueryService>>());

        var response = await service.ListByWorkflowAsync(WorkspaceId, workflowRefId, null, 2, null, CancellationToken.None);

        Assert.True(response.IsSuccess);
        Assert.Equal(2, response.Value.Runs.Count);
        Assert.Null(response.Value.NextCursor);
    }

    [Fact]
    public async Task GetStatsAsync_computes_success_rate_over_finished_runs_only()
    {
        // In-flight runs must not count as failures - an active workflow would look broken.
        var workflowRefId = Guid.NewGuid();
        var runProvider = new Mock<IRunProvider>();
        var definitionProvider = new Mock<IWorkflowDefinitionProvider>();
        definitionProvider.Setup(x => x.GetCurrentByRefIdAsync(workflowRefId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateDefinitionRow(workflowRefId, WorkspaceId));
        runProvider.Setup(x => x.GetStatsAsync(workflowRefId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateStats(total: 10, succeeded: 6, failed: 2, active: 2));
        runProvider.Setup(x => x.GetTopFailuresAsync(workflowRefId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new RunFailureBucketRow { ErrorCode = "WEBHOOK_HTTP_ERROR", NodeId = Guid.NewGuid(), Occurrences = 2, LastSeenAt = DateTime.UtcNow }]);
        runProvider.Setup(x => x.GetNodeTimingsAsync(workflowRefId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var service = new WorkflowRunQueryService(runProvider.Object, Mock.Of<IBranchProvider>(), definitionProvider.Object, Mock.Of<IRunCancellationService>(), Mock.Of<ILogger<WorkflowRunQueryService>>());

        var response = await service.GetStatsAsync(WorkspaceId, workflowRefId, DateTime.UtcNow.AddDays(-7), DateTime.UtcNow, CancellationToken.None);

        Assert.True(response.IsSuccess);
        // 6 succeeded of 8 finished - the 2 active runs are excluded from the denominator.
        Assert.Equal(0.75, response.Value.SuccessRate);
        Assert.Equal(10, response.Value.Counts.Total);
        Assert.Equal(2, response.Value.Counts.Active);
        Assert.Equal("WEBHOOK_HTTP_ERROR", Assert.Single(response.Value.TopFailures).ErrorCode);
    }

    [Fact]
    public async Task GetStatsAsync_reports_no_success_rate_when_nothing_has_finished()
    {
        // A rate of 0 would read as "everything failed" rather than "nothing to report".
        var workflowRefId = Guid.NewGuid();
        var runProvider = new Mock<IRunProvider>();
        var definitionProvider = new Mock<IWorkflowDefinitionProvider>();
        definitionProvider.Setup(x => x.GetCurrentByRefIdAsync(workflowRefId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateDefinitionRow(workflowRefId, WorkspaceId));
        runProvider.Setup(x => x.GetStatsAsync(workflowRefId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateStats(total: 3, succeeded: 0, failed: 0, active: 3));
        runProvider.Setup(x => x.GetTopFailuresAsync(workflowRefId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        runProvider.Setup(x => x.GetNodeTimingsAsync(workflowRefId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var service = new WorkflowRunQueryService(runProvider.Object, Mock.Of<IBranchProvider>(), definitionProvider.Object, Mock.Of<IRunCancellationService>(), Mock.Of<ILogger<WorkflowRunQueryService>>());

        var response = await service.GetStatsAsync(WorkspaceId, workflowRefId, DateTime.UtcNow.AddDays(-7), DateTime.UtcNow, CancellationToken.None);

        Assert.True(response.IsSuccess);
        Assert.Null(response.Value.SuccessRate);
    }

    [Fact]
    public async Task GetStatsAsync_rejects_an_inverted_window()
    {
        var service = new WorkflowRunQueryService(Mock.Of<IRunProvider>(), Mock.Of<IBranchProvider>(), Mock.Of<IWorkflowDefinitionProvider>(), Mock.Of<IRunCancellationService>(), Mock.Of<ILogger<WorkflowRunQueryService>>());

        var response = await service.GetStatsAsync(WorkspaceId, Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow.AddDays(-1), CancellationToken.None);

        Assert.True(response.IsFailure);
        Assert.Equal("INVALID_WINDOW", response.Error.Code);
    }

    [Fact]
    public async Task GetStatsAsync_refuses_a_workflow_in_another_workspace()
    {
        var workflowRefId = Guid.NewGuid();
        var definitionProvider = new Mock<IWorkflowDefinitionProvider>();
        definitionProvider.Setup(x => x.GetCurrentByRefIdAsync(workflowRefId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateDefinitionRow(workflowRefId, WorkspaceId + 1));
        var service = new WorkflowRunQueryService(Mock.Of<IRunProvider>(), Mock.Of<IBranchProvider>(), definitionProvider.Object, Mock.Of<IRunCancellationService>(), Mock.Of<ILogger<WorkflowRunQueryService>>());

        var response = await service.GetStatsAsync(WorkspaceId, workflowRefId, DateTime.UtcNow.AddDays(-7), DateTime.UtcNow, CancellationToken.None);

        Assert.True(response.IsFailure);
    }

    private static RunStatsRow CreateStats(int total, int succeeded, int failed, int active)
    {
        return new RunStatsRow
        {
            TotalRuns = total,
            SucceededCount = succeeded,
            FailedCount = failed,
            PartiallyFailedCount = 0,
            CancelledCount = 0,
            FaultedCount = 0,
            OutOfCreditsCount = 0,
            ActiveCount = active,
            P50DurationMs = 120,
            P95DurationMs = 900,
            MaxDurationMs = 1500,
            AvgDurationMs = 300
        };
    }

    [Fact]
    public async Task ListByWorkspaceAsync_returns_runs_across_every_workflow()
    {
        // The procedure scopes by workspace itself, so no per-workflow ownership check is needed -
        // and none should be attempted.
        var runProvider = new Mock<IRunProvider>();
        var definitionProvider = new Mock<IWorkflowDefinitionProvider>();
        var workflowA = Guid.NewGuid();
        var workflowB = Guid.NewGuid();
        runProvider.Setup(x => x.ListByWorkspaceAsync(WorkspaceId, null, 11, null, It.IsAny<CancellationToken>())).ReturnsAsync([
            CreateRunRow(41, Guid.NewGuid(), workflowA, 5, "Running"),
            CreateRunRow(42, Guid.NewGuid(), workflowB, 5, "Succeeded")
        ]);
        var service = new WorkflowRunQueryService(runProvider.Object, Mock.Of<IBranchProvider>(), definitionProvider.Object, Mock.Of<IRunCancellationService>(), Mock.Of<ILogger<WorkflowRunQueryService>>());

        var response = await service.ListByWorkspaceAsync(WorkspaceId, null, 10, null, CancellationToken.None);

        Assert.True(response.IsSuccess);
        Assert.Equal(2, response.Value.Runs.Count);
        Assert.Null(response.Value.NextCursor);
        Assert.Equal([workflowA, workflowB], response.Value.Runs.Select(r => r.WorkflowDefinitionRefId));
        definitionProvider.VerifyNoOtherCalls();
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
        var service = new WorkflowRunQueryService(runProvider.Object, branchProvider.Object, definitionProvider.Object, cancellationService.Object, Mock.Of<ILogger<WorkflowRunQueryService>>());

        var response = await service.ListByWorkflowAsync(WorkspaceId, workflowRefId, null, 10, null, CancellationToken.None);
        
        Assert.True(response.IsFailure);
        Assert.Equal(ErrorType.Forbidden, response.Error.Type);
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
        var service = new WorkflowRunQueryService(runProvider.Object, branchProvider.Object, definitionProvider.Object, cancellationService.Object, Mock.Of<ILogger<WorkflowRunQueryService>>());

        var detail = await service.GetDetailAsync(WorkspaceId, runRefId, CancellationToken.None);

        Assert.True(detail.IsSuccess);
        Assert.Equal(runRefId, detail.Value.Summary.RefId);
        Assert.Single(detail.Value.Branches);
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
        cancellationService.Setup(x => x.RequestCancellationAsync(63, "missing", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var service = new WorkflowRunQueryService(runProvider.Object, branchProvider.Object, definitionProvider.Object, cancellationService.Object, Mock.Of<ILogger<WorkflowRunQueryService>>());

        var response = await service.CancelAsync(WorkspaceId, runRefId, "missing", CancellationToken.None);

        Assert.True(response.IsSuccess);
        cancellationService.Verify(x => x.RequestCancellationAsync(63, "missing", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CancelAsync_reports_a_conflict_when_the_run_already_finished()
    {
        // Cancelling a finished run used to report success while doing nothing at all.
        var runProvider = new Mock<IRunProvider>();
        var branchProvider = new Mock<IBranchProvider>();
        var definitionProvider = new Mock<IWorkflowDefinitionProvider>();
        var cancellationService = new Mock<IRunCancellationService>();
        var runRefId = Guid.NewGuid();
        var workflowRefId = Guid.NewGuid();
        runProvider.Setup(x => x.GetByRefIdAsync(runRefId, It.IsAny<CancellationToken>())).ReturnsAsync(CreateRunRow(64, runRefId, workflowRefId, 2, "Succeeded"));
        definitionProvider.Setup(x => x.GetByRefIdVersionAsync(workflowRefId, 2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateDefinitionRow(workflowRefId, WorkspaceId));
        cancellationService.Setup(x => x.RequestCancellationAsync(64, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var service = new WorkflowRunQueryService(runProvider.Object, branchProvider.Object, definitionProvider.Object, cancellationService.Object, Mock.Of<ILogger<WorkflowRunQueryService>>());

        var response = await service.CancelAsync(WorkspaceId, runRefId, "too late", CancellationToken.None);

        Assert.True(response.IsFailure);
        Assert.Equal(ErrorType.Conflict, response.Error.Type);
        Assert.Equal("RUN_NOT_CANCELLABLE", response.Error.Code);
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
        var service = new WorkflowRunQueryService(runProvider.Object, branchProvider.Object, definitionProvider.Object, cancellationService.Object, Mock.Of<ILogger<WorkflowRunQueryService>>());

        var response = await service.EnsureRunInWorkspaceAsync(WorkspaceId, runRefId, CancellationToken.None);
        
        Assert.True(response.IsFailure);
        Assert.Equal(ErrorType.Forbidden, response.Error.Type);
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
