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
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;

namespace Wbskt.Workflow.Engine.Host.Tests.ManagementHost;

public sealed class WorkflowHistoryControllerTests
{
    private const int WorkspaceId = 7;
    private static readonly Guid WorkspaceRef = Guid.Parse("99999999-9999-9999-9999-999999999999");

    private static Mock<IAuthServiceClient> AuthClient()
    {
        var authClient = new Mock<IAuthServiceClient>();
        authClient.Setup(x => x.ResolveWorkspaceAsync(WorkspaceRef, Permissions.WorkflowsRead, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<int>.Success(WorkspaceId));
        return authClient;
    }

    [Fact]
    public async Task List_returns_events_from_cursor()
    {
        var runQueryService = new Mock<IWorkflowRunQueryService>();
        var historyProvider = new Mock<IHistoryEventProvider>();
        var authClient = AuthClient();
        var runRefId = Guid.NewGuid();
        runQueryService.Setup(x => x.EnsureRunInWorkspaceAsync(WorkspaceId, runRefId, It.IsAny<CancellationToken>())).ReturnsAsync(Result<int>.Success(12));
        historyProvider.Setup(x => x.GetByRunIdAsync(12, 100, 3, It.IsAny<CancellationToken>())).ReturnsAsync([
            CreateEvent(101, 12, "Started"),
            CreateEvent(102, 12, "Completed")
        ]);
        var controller = new WorkflowHistoryController(runQueryService.Object, historyProvider.Object, authClient.Object, Mock.Of<ILogger<WorkflowHistoryController>>());

        var response = await controller.List(WorkspaceRef, runRefId, 100, 2, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(response.Result);
        var historyListResponse = Assert.IsType<HistoryListResponse>(okResult.Value);
        Assert.Equal(2, historyListResponse.Events.Count);
        Assert.Equal(101, historyListResponse.Events[0].HistoryEventId);
        Assert.Null(historyListResponse.NextCursor);
        authClient.Verify(x => x.ResolveWorkspaceAsync(WorkspaceRef, Permissions.WorkflowsRead, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task List_returns_next_cursor_when_more_available()
    {
        var runQueryService = new Mock<IWorkflowRunQueryService>();
        var historyProvider = new Mock<IHistoryEventProvider>();
        var authClient = AuthClient();
        var runRefId = Guid.NewGuid();
        runQueryService.Setup(x => x.EnsureRunInWorkspaceAsync(WorkspaceId, runRefId, It.IsAny<CancellationToken>())).ReturnsAsync(Result<int>.Success(18));
        historyProvider.Setup(x => x.GetByRunIdAsync(18, 0, 3, It.IsAny<CancellationToken>())).ReturnsAsync([
            CreateEvent(201, 18, "A"),
            CreateEvent(202, 18, "B"),
            CreateEvent(203, 18, "C")
        ]);
        var controller = new WorkflowHistoryController(runQueryService.Object, historyProvider.Object, authClient.Object, Mock.Of<ILogger<WorkflowHistoryController>>());

        var response = await controller.List(WorkspaceRef, runRefId, 0, 2, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(response.Result);
        var historyListResponse = Assert.IsType<HistoryListResponse>(okResult.Value);
        Assert.Equal(2, historyListResponse.Events.Count);
        Assert.Equal(202, historyListResponse.NextCursor);
    }

    [Fact]
    public async Task List_propagates_security_exception_when_run_not_in_workspace()
    {
        var runQueryService = new Mock<IWorkflowRunQueryService>();
        var historyProvider = new Mock<IHistoryEventProvider>();
        var authClient = AuthClient();
        var runRefId = Guid.NewGuid();
        runQueryService.Setup(x => x.EnsureRunInWorkspaceAsync(WorkspaceId, runRefId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<int>.Failure(Error.Unauthorized("RUN_UNAUTHORIZED", "denied")));
        var controller = new WorkflowHistoryController(runQueryService.Object, historyProvider.Object, authClient.Object, Mock.Of<ILogger<WorkflowHistoryController>>());

        var response = await controller.List(WorkspaceRef, runRefId, 0, 200, CancellationToken.None);
        
        Assert.IsType<UnauthorizedObjectResult>(response.Result);
    }

    private static HistoryEventRow CreateEvent(long id, int runId, string kind)
    {
        return new HistoryEventRow
        {
            HistoryEventId = id,
            RunId = runId,
            BranchRefId = null,
            NodeId = null,
            EventKind = kind,
            Severity = "Info",
            PayloadJson = "{}",
            Timestamp = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(id)
        };
    }
}
