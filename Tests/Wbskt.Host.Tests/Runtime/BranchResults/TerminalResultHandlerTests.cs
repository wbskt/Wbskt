using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Runtime;
using Wbskt.Workflow.Runtime.BranchResults;
using static Wbskt.Host.Tests.Runtime.BranchResults.BranchResultTestData;

namespace Wbskt.Host.Tests.Runtime.BranchResults;

public sealed class TerminalResultHandlerTests
{
    private static readonly Guid NodeA = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly List<string> _log = [];

    [Theory]
    [InlineData(BranchTerminalReason.Completed)]
    [InlineData(BranchTerminalReason.Failed)]
    public async Task Completes_the_branch_unless_it_was_cancelled(BranchTerminalReason reason)
    {
        var branches = new RecordingBranchProvider(_log, Branch(NodeA));
        var handler = new TerminalResultHandler(new BranchEndings(branches, new RecordingRunJournal(_log), new RecordingBranchTerminator(_log)), new JoinDeclines(new NoOpJoinAggregatorProvider(), new NoOpRunCancellationService(), new RecordingRunJournal(_log)));

        BranchResultOutcome outcome = await handler.HandleAsync(new NodeExecutionResult.Terminal(reason), Context(Node(NodeA), Definition([Node(NodeA)], []), branches.Current), CancellationToken.None);

        Assert.True(outcome.IsStopped);
        Assert.Equal(new[] { "completed", $"journal:{HistoryEventKind.BranchCompleted}", $"release:{RunId}:1" }, _log);
    }

    [Fact]
    public async Task Cancels_the_branch_where_it_stands_when_the_run_was_cancelled()
    {
        var branches = new RecordingBranchProvider(_log, Branch(NodeA, """{"keep":1}"""));
        var journal = new RecordingRunJournal(_log);
        var handler = new TerminalResultHandler(new BranchEndings(branches, journal, new RecordingBranchTerminator(_log)), new JoinDeclines(new NoOpJoinAggregatorProvider(), new NoOpRunCancellationService(), journal));

        BranchResultOutcome outcome = await handler.HandleAsync(new NodeExecutionResult.Terminal(BranchTerminalReason.Cancelled), Context(Node(NodeA), Definition([Node(NodeA)], []), branches.Current), CancellationToken.None);

        Assert.True(outcome.IsStopped);
        Assert.Equal(new[] { $"update:{BranchStatus.Cancelled}:{NodeA}", $"journal:{HistoryEventKind.BranchCancelled}", $"release:{RunId}:1" }, _log);
        var update = Assert.Single(branches.Updates);
        Assert.Equal("""{"keep":1}""", update.LocalJson);
        Assert.Equal("\"last\"", update.LastOutputJson);
        Assert.Equal(BranchRefId, Assert.Single(journal.Entries).BranchRefId);
    }
}
