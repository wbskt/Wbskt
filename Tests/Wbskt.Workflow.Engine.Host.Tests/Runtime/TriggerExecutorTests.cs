using System.Text.Json;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Triggers;
using Wbskt.Workflow.Abstraction.Models.Triggers;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.NodeExecutors.Triggers;

namespace Wbskt.Workflow.Engine.Host.Tests.Runtime;

public sealed class TriggerExecutorTests
{
    // PassthroughTriggerExecutor is kind-agnostic: it records the trigger payload and continues via
    // "default" regardless of which trigger kind it serves. One Theory covers every kind it is
    // registered for.
    [Theory]
    [InlineData(NodeKind.TriggerClient)]
    [InlineData(NodeKind.TriggerSchedule)]
    [InlineData(NodeKind.TriggerWebhook)]
    [InlineData(NodeKind.TriggerManual)]
    public async Task Passthrough_trigger_returns_default_port_and_trigger_patch(string kind)
    {
        var node = new ManualTriggerNode { NodeId = Guid.NewGuid(), Name = "trigger", Ports = CreatePorts(), Config = new ManualTriggerConfig() };
        NodeExecutionResult result = await new PassthroughTriggerExecutor(kind, new FixedClock()).ExecuteAsync(CreateContext(node), CancellationToken.None);
        AssertContinueResult(result);
    }

    private static NodeContext CreateContext(BaseNode node)
    {
        return new NodeContext
        {
            Branch = new BranchContext(42, 1001, 5, Guid.NewGuid(), 1, node.NodeId.ToString(), 1, new Dictionary<string, JsonElement>(), new Dictionary<string, JsonElement>
            {
                ["clientRefId"] = JsonSerializer.SerializeToElement("serial-1")
            }, "corr-1", DateTime.UtcNow, 9),
            Node = node,
            Providers = new StubProviderComposite(),
            Tick = 1,
            ParentResults = null,
            CancellationToken = CancellationToken.None, IdempotencyKey = "test-key"
        };
    }

    private static IReadOnlyCollection<PortDefinition> CreatePorts() => [new() { PortId = "default", Direction = PortDirection.Output, Label = "Default" }];

    private static void AssertContinueResult(NodeExecutionResult result)
    {
        var continuation = Assert.IsType<NodeExecutionResult.Continue>(result);
        Assert.Equal("default", continuation.OutboundPort);
        Assert.True(continuation.LocalStatePatch.ContainsKey("trigger"));
        Assert.True(continuation.LocalStatePatch.ContainsKey("triggeredAt"));
        Assert.Equal("serial-1", continuation.LocalStatePatch["trigger"].GetProperty("clientRefId").GetString());
        Assert.Equal(new DateTime(2026, 5, 26, 12, 30, 0, DateTimeKind.Utc), continuation.LocalStatePatch["triggeredAt"].GetDateTime());
    }

    private sealed class FixedClock : IClock
    {
        public DateTime UtcNow => new(2026, 5, 26, 12, 30, 0, DateTimeKind.Utc);
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
