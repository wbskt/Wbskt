using System.Globalization;
using System.Text.Json;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Bookmarks;
using Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.NodeExecutors.Controls;

namespace Wbskt.Workflow.Engine.Host.Tests.NodeExecutors.Controls;

public sealed class DelayNodeExecutorTests
{
    private const string DelayUntilKey = "__delay_until";
    private static readonly DateTime T0 = new(2026, 6, 6, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task ExecuteAsync_first_visit_parks_with_timer_at_now_plus_duration()
    {
        var clock = new MutableClock(T0);
        var executor = new DelayNodeExecutor(clock);
        var node = new DelayNode { NodeId = Guid.NewGuid(), Name = "delay", Ports = CreatePorts(), Config = new DelayConfig { Duration = TimeSpan.FromMinutes(5) } };
        NodeContext context = CreateContext(node, new Dictionary<string, JsonElement>());

        NodeExecutionResult result = await executor.ExecuteAsync(context, CancellationToken.None);

        var wait = Assert.IsType<NodeExecutionResult.WaitForBookmark>(result);
        var timer = Assert.IsType<TimerWakeCondition>(wait.Condition);
        Assert.Equal(T0.AddMinutes(5), timer.At);
        Assert.Equal(T0.AddMinutes(5).ToString("O"), wait.LocalStatePatch[DelayUntilKey].GetString());
    }

    [Fact]
    public async Task ExecuteAsync_resume_after_timer_elapsed_continues()
    {
        var clock = new MutableClock(T0.AddMinutes(5).AddSeconds(1));
        var executor = new DelayNodeExecutor(clock);
        var node = new DelayNode { NodeId = Guid.NewGuid(), Name = "delay", Ports = CreatePorts(), Config = new DelayConfig { Duration = TimeSpan.FromMinutes(5) } };
        NodeContext context = CreateContext(node, new Dictionary<string, JsonElement>
        {
            [DelayUntilKey] = JsonSerializer.SerializeToElement(T0.AddMinutes(5).ToString("O"))
        });

        NodeExecutionResult result = await executor.ExecuteAsync(context, CancellationToken.None);

        var cont = Assert.IsType<NodeExecutionResult.Continue>(result);
        Assert.Equal("default", cont.OutboundPort);
    }

    [Fact]
    public async Task ExecuteAsync_zero_duration_continues_immediately()
    {
        var clock = new MutableClock(T0);
        var executor = new DelayNodeExecutor(clock);
        var node = new DelayNode { NodeId = Guid.NewGuid(), Name = "delay", Ports = CreatePorts(), Config = new DelayConfig { Duration = TimeSpan.Zero } };
        NodeContext context = CreateContext(node, new Dictionary<string, JsonElement>());

        NodeExecutionResult result = await executor.ExecuteAsync(context, CancellationToken.None);

        Assert.IsType<NodeExecutionResult.Continue>(result);
    }

    [Fact]
    public async Task ExecuteAsync_redispatched_before_timer_elapsed_reparks_on_same_instant()
    {
        var clock = new MutableClock(T0);
        var executor = new DelayNodeExecutor(clock);
        var node = new DelayNode { NodeId = Guid.NewGuid(), Name = "delay", Ports = CreatePorts(), Config = new DelayConfig { Duration = TimeSpan.FromMinutes(5) } };
        NodeContext context = CreateContext(node, new Dictionary<string, JsonElement>
        {
            [DelayUntilKey] = JsonSerializer.SerializeToElement(T0.AddMinutes(5).ToString("O"))
        });

        NodeExecutionResult result = await executor.ExecuteAsync(context, CancellationToken.None);

        var wait = Assert.IsType<NodeExecutionResult.WaitForBookmark>(result);
        var timer = Assert.IsType<TimerWakeCondition>(wait.Condition);
        Assert.Equal(T0.AddMinutes(5), timer.At);
    }

    private static NodeContext CreateContext(DelayNode node, IReadOnlyDictionary<string, JsonElement> localState)
    {
        return new NodeContext
        {
            Branch = new BranchContext(42, 1001, 5, Guid.NewGuid(), 1, node.NodeId.ToString(), 1, localState, new Dictionary<string, JsonElement>(), "corr-1", T0),
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
            new PortDefinition { PortId = "default", Direction = PortDirection.Output, Label = "Out" }
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
