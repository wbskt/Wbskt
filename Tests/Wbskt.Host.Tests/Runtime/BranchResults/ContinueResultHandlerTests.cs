using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Runtime;
using Wbskt.Workflow.Runtime.BranchResults;
using static Wbskt.Host.Tests.Runtime.BranchResults.BranchResultTestData;

namespace Wbskt.Host.Tests.Runtime.BranchResults;

public sealed class ContinueResultHandlerTests
{
    private static readonly Guid NodeA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid NodeB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly List<string> _log = [];

    [Fact]
    public async Task Moves_the_branch_along_the_port_with_the_patch_merged_and_removed_keys_dropped()
    {
        var branches = new RecordingBranchProvider(_log, Branch(NodeA, """{"keep":1,"drop":2}"""));
        var handler = new ContinueResultHandler(branches, new BranchEndings(branches, new RecordingRunJournal(_log), new RecordingBranchTerminator(_log)));
        var definition = Definition([Node(NodeA), Node(NodeB)], [new Edge((NodeA, "default"), (NodeB, "in"))]);
        var result = new NodeExecutionResult.Continue("default", Patch("added", 3), ["drop"]);

        BranchResultOutcome outcome = await handler.HandleAsync(result, Context(Node(NodeA), definition, branches.Current), CancellationToken.None);

        Assert.False(outcome.IsStopped);
        Assert.Equal(NodeB, outcome.Branch!.NodeId);
        Assert.Equal(new[] { $"update:{BranchStatus.Active}:{NodeB}" }, _log);
        var update = Assert.Single(branches.Updates);
        Assert.Equal(BranchId, update.BranchId);
        Assert.Equal("\"last\"", update.LastOutputJson);
        var localState = LocalState(update.LocalJson);
        Assert.Equal(1, localState["keep"].GetInt32());
        Assert.Equal(3, localState["added"].GetInt32());
        Assert.False(localState.ContainsKey("drop"));
    }

    [Fact]
    public async Task Follows_a_port_that_names_a_node_as_a_jump()
    {
        var branches = new RecordingBranchProvider(_log, Branch(NodeA));
        var handler = new ContinueResultHandler(branches, new BranchEndings(branches, new RecordingRunJournal(_log), new RecordingBranchTerminator(_log)));
        var definition = Definition([Node(NodeA), Node(NodeB)], []);

        BranchResultOutcome outcome = await handler.HandleAsync(NodeExecutionResult.JumpTo(NodeB, Patch("x", 1)), Context(Node(NodeA), definition, branches.Current), CancellationToken.None);

        Assert.Equal(NodeB, outcome.Branch!.NodeId);
    }

    [Fact]
    public async Task Completes_the_branch_when_the_port_leads_nowhere()
    {
        var branches = new RecordingBranchProvider(_log, Branch(NodeA));
        var journal = new RecordingRunJournal(_log);
        var handler = new ContinueResultHandler(branches, new BranchEndings(branches, journal, new RecordingBranchTerminator(_log)));
        var definition = Definition([Node(NodeA)], []);

        BranchResultOutcome outcome = await handler.HandleAsync(new NodeExecutionResult.Continue("default", Patch("x", 1)), Context(Node(NodeA), definition, branches.Current), CancellationToken.None);

        Assert.True(outcome.IsStopped);
        Assert.Equal(new[] { "completed", $"journal:{HistoryEventKind.BranchCompleted}", $"release:{RunId}:1" }, _log);
        var entry = Assert.Single(journal.Entries);
        Assert.Equal(BranchRefId, entry.BranchRefId);
        Assert.Null(entry.NodeId);
    }
}
