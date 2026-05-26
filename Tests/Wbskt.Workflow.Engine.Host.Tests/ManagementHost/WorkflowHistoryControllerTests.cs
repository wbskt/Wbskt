using Moq;
using Wbskt.Management.Host.Controllers;
using Wbskt.Primitives.Exceptions;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;

namespace Wbskt.Workflow.Engine.Host.Tests.ManagementHost;

public sealed class WorkflowHistoryControllerTests
{
    [Fact]
    public async Task List_returns_events_from_cursor()
    {
        var runProvider = new Mock<IRunProvider>();
        var historyProvider = new Mock<IHistoryEventProvider>();
        var runRefId = Guid.NewGuid();
        runProvider.Setup(x => x.FindByRefIdAsync(runRefId, It.IsAny<CancellationToken>())).ReturnsAsync(12);
        historyProvider.Setup(x => x.GetByRunIdAsync(12, 100, 3, It.IsAny<CancellationToken>())).ReturnsAsync([
            CreateEvent(101, 12, "Started"),
            CreateEvent(102, 12, "Completed")
        ]);
        var controller = new WorkflowHistoryController(runProvider.Object, historyProvider.Object);

        var response = await controller.List(runRefId, 100, 2, CancellationToken.None);

        Assert.Equal(2, response.Events.Count);
        Assert.Equal(101, response.Events[0].HistoryEventId);
        Assert.Null(response.NextCursor);
    }

    [Fact]
    public async Task List_returns_next_cursor_when_more_available()
    {
        var runProvider = new Mock<IRunProvider>();
        var historyProvider = new Mock<IHistoryEventProvider>();
        var runRefId = Guid.NewGuid();
        runProvider.Setup(x => x.FindByRefIdAsync(runRefId, It.IsAny<CancellationToken>())).ReturnsAsync(18);
        historyProvider.Setup(x => x.GetByRunIdAsync(18, 0, 3, It.IsAny<CancellationToken>())).ReturnsAsync([
            CreateEvent(201, 18, "A"),
            CreateEvent(202, 18, "B"),
            CreateEvent(203, 18, "C")
        ]);
        var controller = new WorkflowHistoryController(runProvider.Object, historyProvider.Object);

        var response = await controller.List(runRefId, 0, 2, CancellationToken.None);

        Assert.Equal(2, response.Events.Count);
        Assert.Equal(202, response.NextCursor);
    }

    [Fact]
    public async Task List_with_no_runs_throws_NotFoundException()
    {
        var runProvider = new Mock<IRunProvider>();
        var historyProvider = new Mock<IHistoryEventProvider>();
        var runRefId = Guid.NewGuid();
        runProvider.Setup(x => x.FindByRefIdAsync(runRefId, It.IsAny<CancellationToken>())).ReturnsAsync((int?)null);
        var controller = new WorkflowHistoryController(runProvider.Object, historyProvider.Object);

        await Assert.ThrowsAsync<NotFoundException>(() => controller.List(runRefId, 0, 200, CancellationToken.None));
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
