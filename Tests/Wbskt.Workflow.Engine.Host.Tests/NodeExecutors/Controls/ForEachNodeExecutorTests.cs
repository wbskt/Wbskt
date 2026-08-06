using System.Text.Json;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.NodeExecutors.Controls;
using Wbskt.Workflow.Runtime;

namespace Wbskt.Workflow.Engine.Host.Tests.NodeExecutors.Controls;

public sealed class ForEachNodeExecutorTests
{
    private static readonly Guid LoopNodeId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task ExecuteAsync_hands_out_the_first_item_on_the_first_visit()
    {
        var executor = new ForEachNodeExecutor(new ExpressionEvaluator(new SystemClock()));
        NodeContext context = CreateContext(CreateNode(), Items("a", "b", "c"));

        NodeExecutionResult result = await executor.ExecuteAsync(context, CancellationToken.None);

        var cont = Assert.IsType<NodeExecutionResult.Continue>(result);
        Assert.Equal("body", cont.OutboundPort);
        Assert.Equal("a", cont.LocalStatePatch["item"].GetString());
        Assert.Equal(0, cont.LocalStatePatch["index"].GetInt32());
        Assert.Equal(1, cont.LocalStatePatch[IteratorKey].GetInt32());
    }

    [Fact]
    public async Task ExecuteAsync_walks_the_collection_one_item_at_a_time_then_leaves_via_done()
    {
        // Drives the loop the way the branch loop does: each lap re-enters the node with the
        // iterator the previous lap wrote.
        var executor = new ForEachNodeExecutor(new ExpressionEvaluator(new SystemClock()));
        var state = new Dictionary<string, JsonElement>(Items("a", "b", "c"));
        var seen = new List<string>();

        for (int lap = 0; lap < 4; lap++)
        {
            NodeExecutionResult result = await executor.ExecuteAsync(CreateContext(CreateNode(), state), CancellationToken.None);
            var cont = Assert.IsType<NodeExecutionResult.Continue>(result);

            if (cont.OutboundPort == "done")
            {
                // The iterator is dropped so a re-entry (nested or outer loop) starts over.
                Assert.Contains(IteratorKey, cont.RemoveKeys!);
                Assert.Equal(3, lap);
                break;
            }

            Assert.Equal("body", cont.OutboundPort);
            seen.Add(cont.LocalStatePatch["item"].GetString()!);
            foreach (var pair in cont.LocalStatePatch)
            {
                state[pair.Key] = pair.Value;
            }
        }

        Assert.Equal(["a", "b", "c"], seen);
    }

    [Fact]
    public async Task ExecuteAsync_empty_collection_goes_straight_to_done()
    {
        var executor = new ForEachNodeExecutor(new ExpressionEvaluator(new SystemClock()));
        NodeContext context = CreateContext(CreateNode(), Items());

        NodeExecutionResult result = await executor.ExecuteAsync(context, CancellationToken.None);

        var cont = Assert.IsType<NodeExecutionResult.Continue>(result);
        Assert.Equal("done", cont.OutboundPort);
    }

    [Fact]
    public async Task ExecuteAsync_keeps_separate_iterators_per_node()
    {
        // Nested loops must not share a counter, so the key is derived from the node id.
        var other = Guid.Parse("22222222-2222-2222-2222-222222222222");

        Assert.NotEqual(ForEachNodeExecutor.IteratorKey(LoopNodeId), ForEachNodeExecutor.IteratorKey(other));

        var executor = new ForEachNodeExecutor(new ExpressionEvaluator(new SystemClock()));
        var state = new Dictionary<string, JsonElement>(Items("a", "b"))
        {
            // The *other* loop is mid-flight; this one must still start at zero.
            [ForEachNodeExecutor.IteratorKey(other)] = JsonSerializer.SerializeToElement(1)
        };

        NodeExecutionResult result = await executor.ExecuteAsync(CreateContext(CreateNode(), state), CancellationToken.None);

        Assert.Equal("a", Assert.IsType<NodeExecutionResult.Continue>(result).LocalStatePatch["item"].GetString());
    }

    private static string IteratorKey => ForEachNodeExecutor.IteratorKey(LoopNodeId);

    private static ForEachNode CreateNode()
    {
        return new ForEachNode { NodeId = LoopNodeId, Name = "foreach", Ports = CreatePorts(), Config = new ForEachConfig { Collection = "items" } };
    }

    private static Dictionary<string, JsonElement> Items(params string[] values)
    {
        return new Dictionary<string, JsonElement> { ["items"] = JsonSerializer.SerializeToElement(values) };
    }

    private static NodeContext CreateContext(ForEachNode node, IReadOnlyDictionary<string, JsonElement> localState)
    {
        return new NodeContext
        {
            Branch = new BranchContext(42, 1001, 5, Guid.NewGuid(), 1, node.NodeId.ToString(), 1, localState, new Dictionary<string, JsonElement>(), "corr-1", DateTime.UtcNow, 9),
            Node = node,
            Providers = new StubProviderComposite(),
            Tick = 1,
            ParentResults = null,
            CancellationToken = CancellationToken.None, IdempotencyKey = "test-key"
        };
    }

    private static IReadOnlyCollection<PortDefinition> CreatePorts()
    {
        return [
            new PortDefinition { PortId = "in", Direction = PortDirection.Input, Label = "In" },
            new PortDefinition { PortId = "body", Direction = PortDirection.Output, Label = "Body" },
            new PortDefinition { PortId = "done", Direction = PortDirection.Output, Label = "Done" }
        ];
    }

    private sealed class StubProviderComposite : IProviderComposite
    {
        public IWorkflowDefinitionProvider WorkflowDefinition => throw new NotSupportedException();
        public ITriggerRegistrationProvider TriggerRegistration => throw new NotSupportedException();
        public IBookmarkProvider Bookmark => throw new NotSupportedException();
        public ISharedVariableProvider SharedVariable => throw new NotSupportedException();
        public IIdempotencyKeyProvider IdempotencyKey => throw new NotSupportedException();
        public IPendingTriggerEventProvider PendingTriggerEvent => throw new NotSupportedException();
        public IScheduledFireProvider ScheduledFire => throw new NotSupportedException();
    }
}
