using Moq;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Runtime;

namespace Wbskt.Workflow.Engine.Host.Tests.Runtime;

public sealed class RunJournalTests
{
    private static readonly DateTime Now = new(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task An_entry_is_stamped_with_the_clock_and_the_kinds_severity_and_its_payload_is_camel_case_json()
    {
        (RunJournal journal, List<HistoryEventRow> written) = Create();
        Guid branch = Guid.NewGuid();
        Guid node = Guid.NewGuid();

        await journal.AppendAsync(
            new RunJournalEntry(7, HistoryEventKind.NodeFailed) { BranchRefId = branch, NodeId = node, Payload = new { ErrorCode = "BOOM" } },
            CancellationToken.None);

        HistoryEventRow row = Assert.Single(written);
        Assert.Equal(7, row.RunId);
        Assert.Equal(branch, row.BranchRefId);
        Assert.Equal(node, row.NodeId);
        Assert.Equal(HistoryEventKind.NodeFailed, row.EventKind);
        Assert.Equal(HistoryEventKind.SeverityWarn, row.Severity);
        Assert.Equal("{\"errorCode\":\"BOOM\"}", row.PayloadJson);
        Assert.Equal(Now, row.Timestamp);
    }

    [Fact]
    public async Task A_severity_on_the_entry_overrides_the_kinds_default_and_no_payload_stores_null()
    {
        (RunJournal journal, List<HistoryEventRow> written) = Create();

        await journal.AppendAsync(
            new RunJournalEntry(7, HistoryEventKind.RunFinalized) { Severity = HistoryEventKind.SeverityWarn },
            CancellationToken.None);

        HistoryEventRow row = Assert.Single(written);
        Assert.Equal(HistoryEventKind.SeverityWarn, row.Severity);
        Assert.Null(row.PayloadJson);
    }

    private static (RunJournal Journal, List<HistoryEventRow> Written) Create()
    {
        var written = new List<HistoryEventRow>();
        var history = new Mock<IHistoryEventProvider>();
        history
            .Setup(h => h.InsertBatchAsync(It.IsAny<IReadOnlyCollection<HistoryEventRow>>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyCollection<HistoryEventRow>, CancellationToken>((rows, _) => written.AddRange(rows))
            .Returns(Task.CompletedTask);
        return (new RunJournal(history.Object, Mock.Of<TimeProvider>(c => c.GetUtcNow() == new DateTimeOffset(Now))), written);
    }
}
