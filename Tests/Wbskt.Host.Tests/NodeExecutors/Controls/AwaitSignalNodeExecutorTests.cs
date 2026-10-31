using System.Text.Json;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Bookmarks;
using Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Channels;
using Wbskt.Workflow.NodeExecutors.Controls;
using Wbskt.Workflow.Runtime.BranchResults;

namespace Wbskt.Host.Tests.NodeExecutors.Controls;

public sealed class AwaitSignalNodeExecutorTests
{
    private const string ParkedKey = "__await_signal";
    private const string DeadlineKey = "__await_signal_until";
    private static readonly DateTime T0 = new(2026, 6, 6, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid RunRefId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

    [Fact]
    public async Task First_visit_parks_with_signal_scoped_to_run()
    {
        var executor = new AwaitSignalNodeExecutor(new MutableClock(T0), Actions.TestTemplates.Create());
        var node = new AwaitSignalNode { NodeId = Guid.NewGuid(), Name = "await", Ports = Ports(), Config = new AwaitSignalConfig { SignalName = "approve" } };
        NodeContext ctx = CreateContext(node, new Dictionary<string, JsonElement>());

        NodeExecutionResult result = await executor.ExecuteAsync(ctx, CancellationToken.None);

        var wait = Assert.IsType<NodeExecutionResult.WaitForBookmark>(result);
        var signal = Assert.IsType<SignalWakeCondition>(wait.Condition);
        Assert.Equal("approve", signal.Name);
        Assert.Equal(RunRefId.ToString(), signal.Correlation);
        Assert.Null(signal.Ttl);
        // The marker names the node that parked, so another await on the branch does not take it for its own.
        Assert.Equal(node.NodeId.ToString(), wait.LocalStatePatch[ParkedKey].GetProperty("nodeId").GetString());
        Assert.Equal("approve", wait.LocalStatePatch[ParkedKey].GetProperty("waitingOn").GetString());
        Assert.False(wait.LocalStatePatch.ContainsKey(DeadlineKey));
    }

    [Fact]
    public async Task First_visit_with_explicit_correlation_uses_it_as_scope()
    {
        var executor = new AwaitSignalNodeExecutor(new MutableClock(T0), Actions.TestTemplates.Create());
        var node = new AwaitSignalNode { NodeId = Guid.NewGuid(), Name = "await", Ports = Ports(), Config = new AwaitSignalConfig { SignalName = "approve", Correlation = "order-42" } };
        NodeContext ctx = CreateContext(node, new Dictionary<string, JsonElement>());

        var wait = Assert.IsType<NodeExecutionResult.WaitForBookmark>(await executor.ExecuteAsync(ctx, CancellationToken.None));
        var signal = Assert.IsType<SignalWakeCondition>(wait.Condition);
        Assert.Equal("order-42", signal.Correlation);
    }

    [Fact]
    public async Task First_visit_with_ttl_sets_deadline_and_condition_ttl()
    {
        var executor = new AwaitSignalNodeExecutor(new MutableClock(T0), Actions.TestTemplates.Create());
        var node = new AwaitSignalNode { NodeId = Guid.NewGuid(), Name = "await", Ports = Ports(), Config = new AwaitSignalConfig { SignalName = "approve", Ttl = TimeSpan.FromMinutes(10) } };
        NodeContext ctx = CreateContext(node, new Dictionary<string, JsonElement>());

        var wait = Assert.IsType<NodeExecutionResult.WaitForBookmark>(await executor.ExecuteAsync(ctx, CancellationToken.None));
        var signal = Assert.IsType<SignalWakeCondition>(wait.Condition);
        Assert.Equal(TimeSpan.FromMinutes(10), signal.Ttl);
        Assert.Equal(T0.AddMinutes(10).ToString("O"), wait.LocalStatePatch[DeadlineKey].GetString());
    }

    [Fact]
    public async Task Resume_by_signal_continues_default()
    {
        var executor = new AwaitSignalNodeExecutor(new MutableClock(T0), Actions.TestTemplates.Create());
        var node = new AwaitSignalNode { NodeId = Guid.NewGuid(), Name = "await", Ports = Ports(), Config = new AwaitSignalConfig { SignalName = "approve" } };
        NodeContext ctx = CreateContext(node, new Dictionary<string, JsonElement>
        {
            [ParkedKey] = JsonSerializer.SerializeToElement("approve")
        });

        var cont = Assert.IsType<NodeExecutionResult.Continue>(await executor.ExecuteAsync(ctx, CancellationToken.None));
        Assert.Equal("default", cont.OutboundPort);
    }

    [Fact]
    public async Task Resume_after_deadline_continues_timeout_port()
    {
        var executor = new AwaitSignalNodeExecutor(new MutableClock(T0.AddMinutes(11)), Actions.TestTemplates.Create());
        var node = new AwaitSignalNode { NodeId = Guid.NewGuid(), Name = "await", Ports = Ports(), Config = new AwaitSignalConfig { SignalName = "approve", Ttl = TimeSpan.FromMinutes(10) } };
        NodeContext ctx = CreateContext(node, new Dictionary<string, JsonElement>
        {
            [ParkedKey] = JsonSerializer.SerializeToElement("approve"),
            [DeadlineKey] = JsonSerializer.SerializeToElement(T0.AddMinutes(10).ToString("O"))
        });

        var cont = Assert.IsType<NodeExecutionResult.Continue>(await executor.ExecuteAsync(ctx, CancellationToken.None));
        Assert.Equal("timeout", cont.OutboundPort);
        Assert.Equal([ParkedKey, DeadlineKey, "__wake", "__wake_node"], cont.RemoveKeys);
    }

    [Fact]
    public async Task Resume_before_deadline_continues_default()
    {
        var executor = new AwaitSignalNodeExecutor(new MutableClock(T0.AddMinutes(2)), Actions.TestTemplates.Create());
        var node = new AwaitSignalNode { NodeId = Guid.NewGuid(), Name = "await", Ports = Ports(), Config = new AwaitSignalConfig { SignalName = "approve", Ttl = TimeSpan.FromMinutes(10) } };
        NodeContext ctx = CreateContext(node, new Dictionary<string, JsonElement>
        {
            [ParkedKey] = JsonSerializer.SerializeToElement("approve"),
            [DeadlineKey] = JsonSerializer.SerializeToElement(T0.AddMinutes(10).ToString("O"))
        });

        var cont = Assert.IsType<NodeExecutionResult.Continue>(await executor.ExecuteAsync(ctx, CancellationToken.None));
        Assert.Equal("default", cont.OutboundPort);
    }

    [Fact]
    public async Task Resume_with_wake_payload_promotes_body_under_signalPayload()
    {
        var executor = new AwaitSignalNodeExecutor(new MutableClock(T0), Actions.TestTemplates.Create());
        var node = new AwaitSignalNode { NodeId = Guid.NewGuid(), Name = "await", Ports = Ports(), Config = new AwaitSignalConfig { SignalName = "approve" } };
        var wake = JsonSerializer.SerializeToElement(new { signalName = "approve", body = new { approvedBy = "ops" } });
        NodeContext ctx = CreateContext(node, new Dictionary<string, JsonElement>
        {
            [ParkedKey] = JsonSerializer.SerializeToElement("approve"),
            ["__wake"] = wake
        });

        var cont = Assert.IsType<NodeExecutionResult.Continue>(await executor.ExecuteAsync(ctx, CancellationToken.None));
        Assert.Equal("default", cont.OutboundPort);
        Assert.Equal("ops", cont.LocalStatePatch["signalPayload"].GetProperty("approvedBy").GetString());
        // The resume clears the marker, the deadline and the wake so the branch is clean for the next park.
        Assert.Equal([ParkedKey, DeadlineKey, "__wake", "__wake_node"], cont.RemoveKeys);
    }

    [Fact]
    public async Task Resume_with_wake_payload_wins_over_elapsed_deadline()
    {
        // A signal that arrives at/after the deadline still resumes via the signal path, not timeout.
        var executor = new AwaitSignalNodeExecutor(new MutableClock(T0.AddMinutes(20)), Actions.TestTemplates.Create());
        var node = new AwaitSignalNode { NodeId = Guid.NewGuid(), Name = "await", Ports = Ports(), Config = new AwaitSignalConfig { SignalName = "approve", Ttl = TimeSpan.FromMinutes(10) } };
        NodeContext ctx = CreateContext(node, new Dictionary<string, JsonElement>
        {
            [ParkedKey] = JsonSerializer.SerializeToElement("approve"),
            [DeadlineKey] = JsonSerializer.SerializeToElement(T0.AddMinutes(10).ToString("O")),
            ["__wake"] = JsonSerializer.SerializeToElement(new { body = new { ok = true } })
        });

        var cont = Assert.IsType<NodeExecutionResult.Continue>(await executor.ExecuteAsync(ctx, CancellationToken.None));
        Assert.Equal("default", cont.OutboundPort);
    }

    [Fact]
    public async Task Marker_left_by_another_node_is_not_taken_for_this_nodes_resume()
    {
        // Regression: the marker was a bare value under a key shared by every AwaitSignal, so a second
        // AwaitSignal in the branch saw the first one's marker and continued without parking.
        var executor = new AwaitSignalNodeExecutor(new MutableClock(T0), Actions.TestTemplates.Create());
        var node = new AwaitSignalNode { NodeId = Guid.NewGuid(), Name = "await", Ports = Ports(), Config = new AwaitSignalConfig { SignalName = "approve" } };
        Guid other = Guid.NewGuid();
        NodeContext ctx = CreateContext(node, new Dictionary<string, JsonElement>
        {
            [ParkedKey] = JsonSerializer.SerializeToElement(new { nodeId = other.ToString(), waitingOn = "approve" }),
            ["__wake"] = JsonSerializer.SerializeToElement(new { body = new { stale = true } }),
            ["__wake_node"] = JsonSerializer.SerializeToElement(other.ToString())
        });

        var wait = Assert.IsType<NodeExecutionResult.WaitForBookmark>(await executor.ExecuteAsync(ctx, CancellationToken.None));
        Assert.Equal(node.NodeId.ToString(), wait.LocalStatePatch[ParkedKey].GetProperty("nodeId").GetString());
    }

    [Fact]
    public async Task Wake_delivered_for_another_node_re_parks_for_the_rest_of_the_deadline()
    {
        var executor = new AwaitSignalNodeExecutor(new MutableClock(T0.AddMinutes(4)), Actions.TestTemplates.Create());
        var node = new AwaitSignalNode { NodeId = Guid.NewGuid(), Name = "await", Ports = Ports(), Config = new AwaitSignalConfig { SignalName = "approve", Ttl = TimeSpan.FromMinutes(10) } };
        NodeContext ctx = CreateContext(node, new Dictionary<string, JsonElement>
        {
            [ParkedKey] = JsonSerializer.SerializeToElement(new { nodeId = node.NodeId.ToString(), waitingOn = "approve" }),
            [DeadlineKey] = JsonSerializer.SerializeToElement(T0.AddMinutes(10).ToString("O")),
            ["__wake"] = JsonSerializer.SerializeToElement(new { body = new { meantFor = "someone else" } }),
            ["__wake_node"] = JsonSerializer.SerializeToElement(Guid.NewGuid().ToString())
        });

        var wait = Assert.IsType<NodeExecutionResult.WaitForBookmark>(await executor.ExecuteAsync(ctx, CancellationToken.None));
        var signal = Assert.IsType<SignalWakeCondition>(wait.Condition);
        Assert.Equal("approve", signal.Name);
        Assert.Equal(TimeSpan.FromMinutes(6), signal.Ttl);
        // The marker and deadline already on the branch are kept as they are.
        Assert.Empty(wait.LocalStatePatch);
    }

    [Fact]
    public async Task Wake_tagged_for_this_node_resumes_it()
    {
        var executor = new AwaitSignalNodeExecutor(new MutableClock(T0), Actions.TestTemplates.Create());
        var node = new AwaitSignalNode { NodeId = Guid.NewGuid(), Name = "await", Ports = Ports(), Config = new AwaitSignalConfig { SignalName = "approve" } };
        NodeContext ctx = CreateContext(node, new Dictionary<string, JsonElement>
        {
            [ParkedKey] = JsonSerializer.SerializeToElement(new { nodeId = node.NodeId.ToString(), waitingOn = "approve" }),
            ["__wake"] = JsonSerializer.SerializeToElement(new { body = new { approvedBy = "ops" } }),
            ["__wake_node"] = JsonSerializer.SerializeToElement(node.NodeId.ToString())
        });

        var cont = Assert.IsType<NodeExecutionResult.Continue>(await executor.ExecuteAsync(ctx, CancellationToken.None));
        Assert.Equal("default", cont.OutboundPort);
        Assert.Equal("ops", cont.LocalStatePatch["signalPayload"].GetProperty("approvedBy").GetString());
    }

    [Fact]
    public async Task A_correlation_placeholder_is_filled_in_from_the_trigger_before_parking()
    {
        var executor = new AwaitSignalNodeExecutor(new MutableClock(T0), Actions.TestTemplates.Create());
        var node = Correlated("{{trigger.payload.room}}");

        var wait = Assert.IsType<NodeExecutionResult.WaitForBookmark>(await executor.ExecuteAsync(CreateContext(node, Room("greenhouse")), CancellationToken.None));

        Assert.Equal("greenhouse", Assert.IsType<SignalWakeCondition>(wait.Condition).Correlation);
    }

    [Fact]
    public async Task Text_around_a_placeholder_is_kept()
    {
        var executor = new AwaitSignalNodeExecutor(new MutableClock(T0), Actions.TestTemplates.Create());
        var node = Correlated("room-{{trigger.payload.room}}");

        var wait = Assert.IsType<NodeExecutionResult.WaitForBookmark>(await executor.ExecuteAsync(CreateContext(node, Room("loft")), CancellationToken.None));

        Assert.Equal("room-loft", Assert.IsType<SignalWakeCondition>(wait.Condition).Correlation);
    }

    [Fact]
    public async Task Two_runs_for_different_values_each_wait_on_their_own_signal()
    {
        // The regression: both runs parked on the literal "{{trigger.payload.parentRun}}", so a signal
        // sent for one would have woken the other too.
        Guid first = Guid.NewGuid();
        Guid second = Guid.NewGuid();
        var executor = new AwaitSignalNodeExecutor(new MutableClock(T0), Actions.TestTemplates.Create());
        var node = Correlated("{{trigger.payload.parentRun}}");

        string firstKey = await ParkedKeyAsync(executor, node, first);
        string secondKey = await ParkedKeyAsync(executor, node, second);

        var signal = new SignalChannel();
        Assert.Equal(firstKey, signal.CorrelationKey(SignalChannel.Event(first, "approve", default)));
        Assert.Equal(secondKey, signal.CorrelationKey(SignalChannel.Event(second, "approve", default)));
        Assert.NotEqual(firstKey, secondKey);
    }

    [Fact]
    public async Task A_correlation_placeholder_with_no_value_fails_the_step_instead_of_parking()
    {
        var executor = new AwaitSignalNodeExecutor(new MutableClock(T0), Actions.TestTemplates.Create());
        var node = Correlated("{{trigger.payload.room}}");

        var fail = Assert.IsType<NodeExecutionResult.Fail>(await executor.ExecuteAsync(CreateContext(node, new Dictionary<string, JsonElement>()), CancellationToken.None));

        Assert.Equal("SIGNAL_CORRELATION_MISSING", fail.ErrorCode);
        Assert.False(fail.Retryable);
        Assert.Contains("{{trigger.payload.room}}", fail.Message);
    }

    [Fact]
    public async Task A_correlation_that_fills_in_blank_fails_the_step()
    {
        var executor = new AwaitSignalNodeExecutor(new MutableClock(T0), Actions.TestTemplates.Create());
        var node = Correlated("{{trigger.payload.room}}");

        var fail = Assert.IsType<NodeExecutionResult.Fail>(await executor.ExecuteAsync(CreateContext(node, Room("  ")), CancellationToken.None));

        Assert.Equal("SIGNAL_CORRELATION_MISSING", fail.ErrorCode);
    }

    [Fact]
    public async Task A_stale_wake_re_parks_on_the_filled_in_correlation()
    {
        var executor = new AwaitSignalNodeExecutor(new MutableClock(T0.AddMinutes(4)), Actions.TestTemplates.Create());
        var node = new AwaitSignalNode { NodeId = Guid.NewGuid(), Name = "await", Ports = Ports(), Config = new AwaitSignalConfig { SignalName = "approve", Correlation = "{{trigger.payload.room}}", Ttl = TimeSpan.FromMinutes(10) } };
        var state = new Dictionary<string, JsonElement>(Room("greenhouse"))
        {
            [ParkedKey] = JsonSerializer.SerializeToElement(new { nodeId = node.NodeId.ToString(), waitingOn = "approve" }),
            [DeadlineKey] = JsonSerializer.SerializeToElement(T0.AddMinutes(10).ToString("O")),
            ["__wake"] = JsonSerializer.SerializeToElement(new { body = new { meantFor = "someone else" } }),
            ["__wake_node"] = JsonSerializer.SerializeToElement(Guid.NewGuid().ToString())
        };

        var wait = Assert.IsType<NodeExecutionResult.WaitForBookmark>(await executor.ExecuteAsync(CreateContext(node, state), CancellationToken.None));

        Assert.Equal("greenhouse", Assert.IsType<SignalWakeCondition>(wait.Condition).Correlation);
    }

    private static async Task<string> ParkedKeyAsync(AwaitSignalNodeExecutor executor, AwaitSignalNode node, Guid parentRun)
    {
        var state = new Dictionary<string, JsonElement>
        {
            ["trigger"] = JsonSerializer.SerializeToElement(new { payload = new { parentRun = parentRun.ToString() } })
        };
        var wait = Assert.IsType<NodeExecutionResult.WaitForBookmark>(await executor.ExecuteAsync(CreateContext(node, state), CancellationToken.None));
        return WaitForBookmarkResultHandler.GetWakeConditionMatchKey(wait.Condition);
    }

    private static AwaitSignalNode Correlated(string correlation)
    {
        return new AwaitSignalNode { NodeId = Guid.NewGuid(), Name = "await", Ports = Ports(), Config = new AwaitSignalConfig { SignalName = "approve", Correlation = correlation } };
    }

    private static Dictionary<string, JsonElement> Room(string room)
    {
        return new Dictionary<string, JsonElement>
        {
            ["trigger"] = JsonSerializer.SerializeToElement(new { payload = new { room } })
        };
    }

    private static NodeContext CreateContext(AwaitSignalNode node, IReadOnlyDictionary<string, JsonElement> localState)
    {
        return new NodeContext
        {
            Branch = new BranchContext(42, 1001, 5, Guid.NewGuid(), 1, node.NodeId.ToString(), 1, localState, new Dictionary<string, JsonElement>(), "corr-1", T0, 9)
            {
                RunRefId = RunRefId
            },
            Node = node,
            CancellationToken = CancellationToken.None, IdempotencyKey = "test-key"
        };
    }

    private static IReadOnlyCollection<PortDefinition> Ports()
    {
        return [
            new PortDefinition { PortId = "in", Direction = PortDirection.Input, Label = "In" },
            new PortDefinition { PortId = "default", Direction = PortDirection.Output, Label = "Signal" },
            new PortDefinition { PortId = "timeout", Direction = PortDirection.Output, Label = "Timeout" },
            new PortDefinition { PortId = "expired", Direction = PortDirection.Output, Label = "Expired" }
        ];
    }

    private sealed class MutableClock(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => UtcNow;

        public DateTime UtcNow { get; set; } = utcNow;
    }

}
