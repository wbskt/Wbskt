using System.Text.Json;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Bookmarks;
using Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.NodeExecutors.Controls;

namespace Wbskt.Workflow.Engine.Host.Tests.NodeExecutors.Controls;

public sealed class AwaitSignalNodeExecutorTests
{
    private const string ParkedKey = "__await_signal";
    private const string DeadlineKey = "__await_signal_until";
    private static readonly DateTime T0 = new(2026, 6, 6, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid RunRefId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

    [Fact]
    public async Task First_visit_parks_with_signal_scoped_to_run()
    {
        var executor = new AwaitSignalNodeExecutor(new MutableClock(T0));
        var node = new AwaitSignalNode { NodeId = Guid.NewGuid(), Name = "await", Ports = Ports(), Config = new AwaitSignalConfig { SignalName = "approve" } };
        NodeContext ctx = CreateContext(node, new Dictionary<string, JsonElement>());

        NodeExecutionResult result = await executor.ExecuteAsync(ctx, CancellationToken.None);

        var wait = Assert.IsType<NodeExecutionResult.WaitForBookmark>(result);
        var signal = Assert.IsType<SignalWakeCondition>(wait.Condition);
        Assert.Equal("approve", signal.Name);
        Assert.Equal(RunRefId.ToString(), signal.Correlation);
        Assert.Null(signal.Ttl);
        Assert.Equal("approve", wait.LocalStatePatch[ParkedKey].GetString());
        Assert.False(wait.LocalStatePatch.ContainsKey(DeadlineKey));
    }

    [Fact]
    public async Task First_visit_with_explicit_correlation_uses_it_as_scope()
    {
        var executor = new AwaitSignalNodeExecutor(new MutableClock(T0));
        var node = new AwaitSignalNode { NodeId = Guid.NewGuid(), Name = "await", Ports = Ports(), Config = new AwaitSignalConfig { SignalName = "approve", Correlation = "order-42" } };
        NodeContext ctx = CreateContext(node, new Dictionary<string, JsonElement>());

        var wait = Assert.IsType<NodeExecutionResult.WaitForBookmark>(await executor.ExecuteAsync(ctx, CancellationToken.None));
        var signal = Assert.IsType<SignalWakeCondition>(wait.Condition);
        Assert.Equal("order-42", signal.Correlation);
    }

    [Fact]
    public async Task First_visit_with_ttl_sets_deadline_and_condition_ttl()
    {
        var executor = new AwaitSignalNodeExecutor(new MutableClock(T0));
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
        var executor = new AwaitSignalNodeExecutor(new MutableClock(T0));
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
        var executor = new AwaitSignalNodeExecutor(new MutableClock(T0.AddMinutes(11)));
        var node = new AwaitSignalNode { NodeId = Guid.NewGuid(), Name = "await", Ports = Ports(), Config = new AwaitSignalConfig { SignalName = "approve", Ttl = TimeSpan.FromMinutes(10), OnTimeout = "expired" } };
        NodeContext ctx = CreateContext(node, new Dictionary<string, JsonElement>
        {
            [ParkedKey] = JsonSerializer.SerializeToElement("approve"),
            [DeadlineKey] = JsonSerializer.SerializeToElement(T0.AddMinutes(10).ToString("O"))
        });

        var cont = Assert.IsType<NodeExecutionResult.Continue>(await executor.ExecuteAsync(ctx, CancellationToken.None));
        Assert.Equal("expired", cont.OutboundPort);
    }

    [Fact]
    public async Task Resume_before_deadline_continues_default()
    {
        var executor = new AwaitSignalNodeExecutor(new MutableClock(T0.AddMinutes(2)));
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
        var executor = new AwaitSignalNodeExecutor(new MutableClock(T0));
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
    }

    [Fact]
    public async Task Resume_with_wake_payload_wins_over_elapsed_deadline()
    {
        // A signal that arrives at/after the deadline still resumes via the signal path, not timeout.
        var executor = new AwaitSignalNodeExecutor(new MutableClock(T0.AddMinutes(20)));
        var node = new AwaitSignalNode { NodeId = Guid.NewGuid(), Name = "await", Ports = Ports(), Config = new AwaitSignalConfig { SignalName = "approve", Ttl = TimeSpan.FromMinutes(10), OnTimeout = "expired" } };
        NodeContext ctx = CreateContext(node, new Dictionary<string, JsonElement>
        {
            [ParkedKey] = JsonSerializer.SerializeToElement("approve"),
            [DeadlineKey] = JsonSerializer.SerializeToElement(T0.AddMinutes(10).ToString("O")),
            ["__wake"] = JsonSerializer.SerializeToElement(new { body = new { ok = true } })
        });

        var cont = Assert.IsType<NodeExecutionResult.Continue>(await executor.ExecuteAsync(ctx, CancellationToken.None));
        Assert.Equal("default", cont.OutboundPort);
    }

    private static NodeContext CreateContext(AwaitSignalNode node, IReadOnlyDictionary<string, JsonElement> localState)
    {
        return new NodeContext
        {
            Branch = new BranchContext(42, 1001, 5, Guid.NewGuid(), 1, node.NodeId.ToString(), 1, localState, new Dictionary<string, JsonElement>(), "corr-1", T0)
            {
                RunRefId = RunRefId
            },
            Node = node,
            Providers = new StubProviderComposite(),
            Tick = 1,
            ParentResults = null,
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

    private sealed class MutableClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow { get; set; } = utcNow;
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
