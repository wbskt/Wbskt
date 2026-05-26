using System.Text.Json;
using Moq;
using Wbskt.Management.Host.Controllers;
using Wbskt.Management.Host.Services;
using Wbskt.Management.Models.Workflow;
using Wbskt.Primitives.Exceptions;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Engine.Host.Tests.ManagementHost;

public sealed class WorkflowRunsControllerTests
{
    [Fact]
    public async Task List_calls_service_with_params()
    {
        var service = new Mock<IWorkflowRunQueryService>();
        var workflowRefId = Guid.NewGuid();
        var expected = new RunListResponse([], null);
        service.Setup(x => x.ListByWorkflowAsync(workflowRefId, "Running", 25, 12, It.IsAny<CancellationToken>())).ReturnsAsync(expected);
        var controller = new WorkflowRunsController(service.Object);

        var actual = await controller.List(workflowRefId, "Running", 25, 12, CancellationToken.None);

        Assert.Equal(expected, actual);
        service.Verify(x => x.ListByWorkflowAsync(workflowRefId, "Running", 25, 12, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Get_returns_detail_dto()
    {
        var service = new Mock<IWorkflowRunQueryService>();
        var runRefId = Guid.NewGuid();
        var detail = new RunDetailDto(new RunSummaryDto(runRefId, Guid.NewGuid(), 3, "Running", "corr", DateTime.UtcNow, null), []);
        service.Setup(x => x.GetDetailAsync(runRefId, It.IsAny<CancellationToken>())).ReturnsAsync(detail);
        var controller = new WorkflowRunsController(service.Object);

        var actual = await controller.Get(runRefId, CancellationToken.None);

        Assert.Equal(detail, actual);
        service.Verify(x => x.GetDetailAsync(runRefId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Cancel_calls_service()
    {
        var service = new Mock<IWorkflowRunQueryService>();
        var runRefId = Guid.NewGuid();
        var controller = new WorkflowRunsController(service.Object);

        await controller.Cancel(runRefId, new CancelRunRequest("operator request"), CancellationToken.None);

        service.Verify(x => x.CancelAsync(runRefId, "operator request", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Signal_calls_inbound_hub_with_signal_channel()
    {
        var service = new Mock<IWorkflowRunQueryService>();
        var inboundHub = new Mock<IInboundHub>();
        var runRefId = Guid.NewGuid();
        inboundHub.Setup(x => x.HandleAsync(It.IsAny<InboundEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TriggerDispatchResult(TriggerDispatchOutcome.ResumedBookmark, 77, 88, "matched"));
        var controller = new WorkflowRunsController(service.Object, inboundHub.Object);

        var response = await controller.Signal(runRefId, "wake", new SignalRequest("wake", JsonSerializer.SerializeToElement(new { ready = true })), CancellationToken.None);

        Assert.True(response.Matched);
        Assert.Equal("ResumedBookmark", response.Outcome);
        inboundHub.Verify(x => x.HandleAsync(It.Is<InboundEvent>(evt => evt.ChannelKind == "signal" && evt.CorrelationKey == $"{runRefId}:wake"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ListByWorkflowAsync_maps_runs_to_response()
    {
        var runProvider = new Mock<IRunProvider>();
        var branchProvider = new Mock<IBranchProvider>();
        var cancellationService = new Mock<IRunCancellationService>();
        var workflowRefId = Guid.NewGuid();
        runProvider.Setup(x => x.ListByWorkflowAsync(workflowRefId, "Running", 2, null, It.IsAny<CancellationToken>())).ReturnsAsync([
            CreateRunRow(41, Guid.NewGuid(), workflowRefId, 5, "Running"),
            CreateRunRow(42, Guid.NewGuid(), workflowRefId, 5, "Completed")
        ]);
        var service = new WorkflowRunQueryService(runProvider.Object, branchProvider.Object, cancellationService.Object);

        var response = await service.ListByWorkflowAsync(workflowRefId, "Running", 2, null, CancellationToken.None);

        Assert.Equal(2, response.Runs.Count);
        Assert.Equal(42, response.NextCursor);
        Assert.Equal(workflowRefId, response.Runs[0].WorkflowDefinitionRefId);
    }

    [Fact]
    public async Task GetDetailAsync_returns_run_with_branches()
    {
        var runProvider = new Mock<IRunProvider>();
        var branchProvider = new Mock<IBranchProvider>();
        var cancellationService = new Mock<IRunCancellationService>();
        var runRefId = Guid.NewGuid();
        var run = CreateRunRow(51, runRefId, Guid.NewGuid(), 6, "Running");
        runProvider.Setup(x => x.GetByRefIdAsync(runRefId, It.IsAny<CancellationToken>())).ReturnsAsync(run);
        branchProvider.Setup(x => x.GetAllByRunIdAsync(51, It.IsAny<CancellationToken>())).ReturnsAsync([
            new BranchRow { Id = 100, RefId = Guid.NewGuid(), RunId = 51, ParentBranchId = null, ForkCohortId = null, NodeId = Guid.NewGuid(), Status = "Running", PendingTakePort = null, LocalJson = "{}", LastOutputJson = null, CompensationStackJson = null, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, RowVersion = [1] }
        ]);
        var service = new WorkflowRunQueryService(runProvider.Object, branchProvider.Object, cancellationService.Object);

        var detail = await service.GetDetailAsync(runRefId, CancellationToken.None);

        Assert.Equal(runRefId, detail.Summary.RefId);
        Assert.Single(detail.Branches);
    }

    [Fact]
    public async Task CancelAsync_throws_not_found_when_run_missing()
    {
        var runProvider = new Mock<IRunProvider>();
        var branchProvider = new Mock<IBranchProvider>();
        var cancellationService = new Mock<IRunCancellationService>();
        var runRefId = Guid.NewGuid();
        runProvider.Setup(x => x.FindByRefIdAsync(runRefId, It.IsAny<CancellationToken>())).ReturnsAsync((int?)null);
        var service = new WorkflowRunQueryService(runProvider.Object, branchProvider.Object, cancellationService.Object);

        await Assert.ThrowsAsync<NotFoundException>(() => service.CancelAsync(runRefId, "missing", CancellationToken.None));
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
