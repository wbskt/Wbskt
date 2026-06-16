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
    [Fact]
    public async Task DeviceTriggerExecutor_returns_default_port_and_trigger_patch()
    {
        NodeExecutionResult result = await new DeviceTriggerExecutor(new FixedClock()).ExecuteAsync(CreateContext(new DeviceTriggerNode(Guid.NewGuid(), "device", CreatePorts(), new DeviceTriggerConfig("device-1", "telemetry"))), CancellationToken.None);
        AssertContinueResult(result);
    }

    [Fact]
    public async Task ScheduleTriggerExecutor_returns_default_port_and_trigger_patch()
    {
        NodeExecutionResult result = await new ScheduleTriggerExecutor(new FixedClock()).ExecuteAsync(CreateContext(new ScheduleTriggerNode(Guid.NewGuid(), "schedule", CreatePorts(), new ScheduleTriggerConfig("* * * * *"))), CancellationToken.None);
        AssertContinueResult(result);
    }

    [Fact]
    public async Task WebhookTriggerExecutor_returns_default_port_and_trigger_patch()
    {
        NodeExecutionResult result = await new WebhookTriggerExecutor(new FixedClock()).ExecuteAsync(CreateContext(new WebhookTriggerNode(Guid.NewGuid(), "webhook", CreatePorts(), new WebhookTriggerConfig("/hook", "POST"))), CancellationToken.None);
        AssertContinueResult(result);
    }

    [Fact]
    public async Task ManualTriggerExecutor_returns_default_port_and_trigger_patch()
    {
        NodeExecutionResult result = await new ManualTriggerExecutor(new FixedClock()).ExecuteAsync(CreateContext(new ManualTriggerNode(Guid.NewGuid(), "manual", CreatePorts(), new ManualTriggerConfig("start"))), CancellationToken.None);
        AssertContinueResult(result);
    }

    private static NodeContext CreateContext(BaseNode node)
    {
        return new NodeContext
        {
            Branch = new BranchContext(42, 1001, 5, Guid.NewGuid(), 1, node.NodeId.ToString(), 1, new Dictionary<string, JsonElement>(), new Dictionary<string, JsonElement>
            {
                ["deviceSerial"] = JsonSerializer.SerializeToElement("serial-1")
            }, "corr-1", DateTime.UtcNow),
            Node = node,
            Providers = new StubProviderComposite(),
            Tick = 1,
            ParentResults = null,
            CancellationToken = CancellationToken.None, IdempotencyKey = "test-key"
        };
    }

    private static IReadOnlyCollection<PortDefinition> CreatePorts() => [new("default", PortDirection.Output, "Default")];

    private static void AssertContinueResult(NodeExecutionResult result)
    {
        var continuation = Assert.IsType<NodeExecutionResult.Continue>(result);
        Assert.Equal("default", continuation.OutboundPort);
        Assert.True(continuation.LocalStatePatch.ContainsKey("trigger"));
        Assert.True(continuation.LocalStatePatch.ContainsKey("triggeredAt"));
        Assert.Equal("serial-1", continuation.LocalStatePatch["trigger"].GetProperty("deviceSerial").GetString());
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
