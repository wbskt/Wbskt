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
    public async Task Client_trigger_returns_default_port_and_trigger_patch()
    {
        NodeExecutionResult result = await new PassthroughTriggerExecutor(NodeKind.TriggerClient, new FixedClock()).ExecuteAsync(CreateContext(new ClientTriggerNode { NodeId = Guid.NewGuid(), Name = "client", Ports = CreatePorts(), Config = new ClientTriggerConfig { ClientRef = "client-1", Type = "telemetry" } }), CancellationToken.None);
        AssertContinueResult(result);
    }

    [Fact]
    public async Task Schedule_trigger_returns_default_port_and_trigger_patch()
    {
        NodeExecutionResult result = await new PassthroughTriggerExecutor(NodeKind.TriggerSchedule, new FixedClock()).ExecuteAsync(CreateContext(new ScheduleTriggerNode { NodeId = Guid.NewGuid(), Name = "schedule", Ports = CreatePorts(), Config = new ScheduleTriggerConfig { Cron = "* * * * *" } }), CancellationToken.None);
        AssertContinueResult(result);
    }

    [Fact]
    public async Task Webhook_trigger_returns_default_port_and_trigger_patch()
    {
        NodeExecutionResult result = await new PassthroughTriggerExecutor(NodeKind.TriggerWebhook, new FixedClock()).ExecuteAsync(CreateContext(new WebhookTriggerNode { NodeId = Guid.NewGuid(), Name = "webhook", Ports = CreatePorts(), Config = new WebhookTriggerConfig { Path = "/hook", Method = "POST" } }), CancellationToken.None);
        AssertContinueResult(result);
    }

    [Fact]
    public async Task Manual_trigger_returns_default_port_and_trigger_patch()
    {
        NodeExecutionResult result = await new PassthroughTriggerExecutor(NodeKind.TriggerManual, new FixedClock()).ExecuteAsync(CreateContext(new ManualTriggerNode { NodeId = Guid.NewGuid(), Name = "manual", Ports = CreatePorts(), Config = new ManualTriggerConfig { Description = "start" } }), CancellationToken.None);
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
