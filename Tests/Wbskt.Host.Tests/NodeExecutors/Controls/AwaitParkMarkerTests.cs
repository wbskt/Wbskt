using System.Text.Json;
using Moq;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Bookmarks;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.NodeExecutors.Controls;
using Wbskt.Workflow.Runtime;

namespace Wbskt.Host.Tests.NodeExecutors.Controls;

/// <summary>
/// Several awaits on one branch, driven through the real executors with the branch's local state
/// carried between visits the way the result handlers and the bookmark resumer carry it.
///
/// Regression: the park markers were per-kind and never cleared, so a second await of the same kind
/// in a branch - or the same await revisited in a loop - took the first one's marker for its own,
/// never parked, and continued on the stale wake.
/// </summary>
public sealed class AwaitParkMarkerTests
{
    private static readonly DateTime T0 = new(2026, 6, 6, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid RunRefId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly Guid ChildRunRefId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");

    [Fact]
    public async Task Two_AwaitSignal_nodes_in_sequence_each_park()
    {
        var executor = new AwaitSignalNodeExecutor(new MutableClock(T0), Actions.TestTemplates.Create());
        AwaitSignalNode first = Await("first");
        AwaitSignalNode second = Await("second");
        var state = new Dictionary<string, JsonElement>();

        await ParkAndWakeAsync(executor, first, state, new { body = new { from = "first" } });

        var park = Assert.IsType<NodeExecutionResult.WaitForBookmark>(await executor.ExecuteAsync(Context(second, state), CancellationToken.None));
        Assert.Equal("second", Assert.IsType<SignalWakeCondition>(park.Condition).Name);
        Apply(state, park.LocalStatePatch, null);

        Wake(state, second.NodeId, new { body = new { from = "second" } });
        var resumed = Assert.IsType<NodeExecutionResult.Continue>(await executor.ExecuteAsync(Context(second, state), CancellationToken.None));
        Assert.Equal("default", resumed.OutboundPort);
        Assert.Equal("second", resumed.LocalStatePatch["signalPayload"].GetProperty("from").GetString());
    }

    [Fact]
    public async Task AwaitSignal_inside_a_ForEach_parks_on_every_iteration()
    {
        var awaitExecutor = new AwaitSignalNodeExecutor(new MutableClock(T0), Actions.TestTemplates.Create());
        var loopExecutor = new ForEachNodeExecutor(new ExpressionEvaluator(TimeProvider.System));
        var loop = new ForEachNode { NodeId = Guid.NewGuid(), Name = "loop", Ports = [], Config = new ForEachConfig { Collection = "items" } };
        AwaitSignalNode wait = Await("approve");
        var state = new Dictionary<string, JsonElement> { ["items"] = JsonSerializer.SerializeToElement(new[] { "a", "b", "c" }) };

        int parks = 0;
        while (true)
        {
            var lap = Assert.IsType<NodeExecutionResult.Continue>(await loopExecutor.ExecuteAsync(Context(loop, state), CancellationToken.None));
            Apply(state, lap.LocalStatePatch, lap.RemoveKeys);
            if (lap.OutboundPort == "done")
            {
                break;
            }

            string item = lap.LocalStatePatch["item"].GetString()!;
            var park = Assert.IsType<NodeExecutionResult.WaitForBookmark>(await awaitExecutor.ExecuteAsync(Context(wait, state), CancellationToken.None));
            parks++;
            Apply(state, park.LocalStatePatch, null);

            Wake(state, wait.NodeId, new { body = new { item } });
            var resumed = Assert.IsType<NodeExecutionResult.Continue>(await awaitExecutor.ExecuteAsync(Context(wait, state), CancellationToken.None));
            Assert.Equal(item, resumed.LocalStatePatch["signalPayload"].GetProperty("item").GetString());
            Apply(state, resumed.LocalStatePatch, resumed.RemoveKeys);
        }

        Assert.Equal(3, parks);
    }

    [Fact]
    public async Task Ttl_timeout_after_an_earlier_successful_await_exits_via_timeout()
    {
        var clock = new MutableClock(T0);
        var executor = new AwaitSignalNodeExecutor(clock, Actions.TestTemplates.Create());
        AwaitSignalNode first = Await("first");
        AwaitSignalNode second = Await("second", TimeSpan.FromMinutes(10));
        var state = new Dictionary<string, JsonElement>();

        await ParkAndWakeAsync(executor, first, state, new { body = new { ok = true } });

        var park = Assert.IsType<NodeExecutionResult.WaitForBookmark>(await executor.ExecuteAsync(Context(second, state), CancellationToken.None));
        Apply(state, park.LocalStatePatch, null);

        // The TTL passes with no signal: the node runs again with no wake on the branch. The first
        // await's wake used to linger here and send this exit through "default".
        clock.UtcNow = T0.AddMinutes(11);
        var timedOut = Assert.IsType<NodeExecutionResult.Continue>(await executor.ExecuteAsync(Context(second, state), CancellationToken.None));
        Assert.Equal("timeout", timedOut.OutboundPort);
        Assert.False(timedOut.LocalStatePatch.ContainsKey("signalPayload"));
    }

    [Fact]
    public async Task SubWorkflow_after_AwaitSignal_parks_and_gets_its_own_child_result()
    {
        var awaitExecutor = new AwaitSignalNodeExecutor(new MutableClock(T0), Actions.TestTemplates.Create());
        var hub = new Mock<IInboundHub>();
        hub.Setup(h => h.HandleAsync(It.IsAny<InboundEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TriggerDispatchResult(TriggerDispatchOutcome.StartedRun, 99, null, "ok"));
        var runProvider = new Mock<IRunProvider>();
        runProvider.Setup(r => r.GetByIdAsync(99, It.IsAny<CancellationToken>())).ReturnsAsync(ChildRun());
        var subExecutor = new SubWorkflowNodeExecutor(hub.Object, runProvider.Object, new MutableClock(T0));
        AwaitSignalNode wait = Await("approve");
        var sub = new SubWorkflowNode { NodeId = Guid.NewGuid(), Name = "sub", Ports = [], Config = new SubWorkflowConfig { WorkflowRefId = Guid.NewGuid() } };
        var state = new Dictionary<string, JsonElement>();

        await ParkAndWakeAsync(awaitExecutor, wait, state, new { signalName = "approve", body = new { approvedBy = "ops" } });

        // The signal's wake must not read as the child's completion.
        var park = Assert.IsType<NodeExecutionResult.WaitForBookmark>(await subExecutor.ExecuteAsync(Context(sub, state), CancellationToken.None));
        Assert.Equal(ChildRunRefId, Assert.IsType<ChildRunCompletedWakeCondition>(park.Condition).ChildRunId);
        Apply(state, park.LocalStatePatch, null);

        Wake(state, sub.NodeId, new { childRunRefId = ChildRunRefId, status = "Succeeded" });
        var resumed = Assert.IsType<NodeExecutionResult.Continue>(await subExecutor.ExecuteAsync(Context(sub, state), CancellationToken.None));
        JsonElement childResult = resumed.LocalStatePatch["childResult"];
        Assert.Equal("Succeeded", childResult.GetProperty("status").GetString());
        Assert.False(childResult.TryGetProperty("signalName", out _));
    }

    private static async Task ParkAndWakeAsync(INodeExecutor executor, BaseNode node, Dictionary<string, JsonElement> state, object wake)
    {
        var park = Assert.IsType<NodeExecutionResult.WaitForBookmark>(await executor.ExecuteAsync(Context(node, state), CancellationToken.None));
        Apply(state, park.LocalStatePatch, null);

        Wake(state, node.NodeId, wake);
        var resumed = Assert.IsType<NodeExecutionResult.Continue>(await executor.ExecuteAsync(Context(node, state), CancellationToken.None));
        Assert.Equal("default", resumed.OutboundPort);
        Apply(state, resumed.LocalStatePatch, resumed.RemoveKeys);
    }

    // What the bookmark resumer writes when an event matches the node's bookmark.
    private static void Wake(Dictionary<string, JsonElement> state, Guid nodeId, object payload)
    {
        state[BookmarkResumer.WakeKey] = JsonSerializer.SerializeToElement(payload);
        state[BookmarkResumer.WakeNodeKey] = JsonSerializer.SerializeToElement(nodeId.ToString());
    }

    // The same merge the result handlers apply: removals first, then the patch.
    private static void Apply(Dictionary<string, JsonElement> state, IReadOnlyDictionary<string, JsonElement> patch, IReadOnlyCollection<string>? removeKeys)
    {
        IReadOnlyDictionary<string, JsonElement> merged = BranchLocalState.Merge(BranchLocalState.Serialize(state), patch, removeKeys);
        state.Clear();
        foreach (var pair in merged)
        {
            state[pair.Key] = pair.Value;
        }
    }

    private static AwaitSignalNode Await(string signal, TimeSpan? ttl = null)
    {
        return new AwaitSignalNode
        {
            NodeId = Guid.NewGuid(),
            Name = signal,
            Ports =
            [
                new PortDefinition { PortId = "in", Direction = PortDirection.Input, Label = "In" },
                new PortDefinition { PortId = "default", Direction = PortDirection.Output, Label = "Signal" },
                new PortDefinition { PortId = "timeout", Direction = PortDirection.Output, Label = "Timeout" }
            ],
            Config = new AwaitSignalConfig { SignalName = signal, Ttl = ttl }
        };
    }

    private static NodeContext Context(BaseNode node, IReadOnlyDictionary<string, JsonElement> localState)
    {
        return new NodeContext
        {
            Branch = new BranchContext(42, 1001, 5, Guid.NewGuid(), 1, node.NodeId.ToString(), 1, new Dictionary<string, JsonElement>(localState), new Dictionary<string, JsonElement>(), "corr-1", T0, 9)
            {
                RunRefId = RunRefId
            },
            Node = node,
            CancellationToken = CancellationToken.None, IdempotencyKey = "test-key"
        };
    }

    private static RunRow ChildRun() => new()
    {
        Id = 99, RefId = ChildRunRefId, WorkflowDefinitionId = 7, WorkflowRefId = Guid.NewGuid(), WorkflowVersion = 1,
        TriggerNodeId = Guid.NewGuid(), CorrelationKey = "manual:x", Status = "Running",
        StartedAt = T0, CompletedAt = null, CancellationRequestedAt = null, CancellationReason = null,
        CreditBudget = 0m, CreatedAt = T0
    };

    private sealed class MutableClock(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => UtcNow;

        public DateTime UtcNow { get; set; } = utcNow;
    }
}
