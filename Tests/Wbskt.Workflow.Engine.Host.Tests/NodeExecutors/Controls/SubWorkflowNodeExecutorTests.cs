using System.Text.Json;
using Moq;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Bookmarks;
using Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.NodeExecutors.Controls;

namespace Wbskt.Workflow.Engine.Host.Tests.NodeExecutors.Controls;

public sealed class SubWorkflowNodeExecutorTests
{
    private const string ParkedKey = "__subworkflow";
    private static readonly Guid ChildRefId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid ChildRunRefId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");

    [Fact]
    public async Task First_visit_starts_child_via_hub_and_parks_on_child_completed()
    {
        var hub = new Mock<IInboundHub>();
        hub.Setup(h => h.HandleAsync(It.IsAny<InboundEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TriggerDispatchResult(TriggerDispatchOutcome.StartedRun, 99, null, "ok"));
        var runProvider = new Mock<IRunProvider>();
        runProvider.Setup(r => r.GetByIdAsync(99, It.IsAny<CancellationToken>())).ReturnsAsync(RunRow(ChildRunRefId));
        var executor = new SubWorkflowNodeExecutor(hub.Object, runProvider.Object, new FixedClock());
        NodeContext ctx = Context(new Dictionary<string, JsonElement>());

        NodeExecutionResult result = await executor.ExecuteAsync(ctx, CancellationToken.None);

        var wait = Assert.IsType<NodeExecutionResult.WaitForBookmark>(result);
        var child = Assert.IsType<ChildRunCompletedWakeCondition>(wait.Condition);
        Assert.Equal(ChildRunRefId, child.ChildRunId);
        Assert.Equal(ChildRunRefId.ToString(), wait.LocalStatePatch[ParkedKey].GetString());
        hub.Verify(h => h.HandleAsync(
            It.Is<InboundEvent>(e => e.ChannelKind == "manual"
                && e.Payload["workflowDefinitionRefId"].GetString() == ChildRefId.ToString()),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task First_visit_fails_when_child_did_not_start()
    {
        var hub = new Mock<IInboundHub>();
        hub.Setup(h => h.HandleAsync(It.IsAny<InboundEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TriggerDispatchResult(TriggerDispatchOutcome.NoRegistration, null, null, "no-reg"));
        var executor = new SubWorkflowNodeExecutor(hub.Object, new Mock<IRunProvider>().Object, new FixedClock());
        NodeContext ctx = Context(new Dictionary<string, JsonElement>());

        var fail = Assert.IsType<NodeExecutionResult.Fail>(await executor.ExecuteAsync(ctx, CancellationToken.None));
        Assert.Equal("SUBWORKFLOW_NOT_STARTED", fail.ErrorCode);
    }

    [Fact]
    public async Task Resume_continues_default_promoting_child_result()
    {
        var executor = new SubWorkflowNodeExecutor(new Mock<IInboundHub>().Object, new Mock<IRunProvider>().Object, new FixedClock());
        var wake = JsonSerializer.SerializeToElement(new { childRunRefId = ChildRunRefId, status = "Succeeded" });
        NodeContext ctx = Context(new Dictionary<string, JsonElement>
        {
            [ParkedKey] = JsonSerializer.SerializeToElement(ChildRunRefId.ToString()),
            ["__wake"] = wake
        });

        var cont = Assert.IsType<NodeExecutionResult.Continue>(await executor.ExecuteAsync(ctx, CancellationToken.None));
        Assert.Equal("default", cont.OutboundPort);
        Assert.Equal("Succeeded", cont.LocalStatePatch["childResult"].GetProperty("status").GetString());
    }

    private static NodeContext Context(IReadOnlyDictionary<string, JsonElement> localState)
    {
        var node = new SubWorkflowNode(Guid.NewGuid(), "sub", [], new SubWorkflowConfig(ChildRefId));
        return new NodeContext
        {
            Branch = new BranchContext(42, 1001, 5, Guid.NewGuid(), 1, node.NodeId.ToString(), 1, localState, new Dictionary<string, JsonElement>(), "corr-1", new DateTime(2026, 6, 6, 12, 0, 0, DateTimeKind.Utc))
            {
                RunRefId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd")
            },
            Node = node,
            Providers = new StubProviderComposite(),
            Tick = 1,
            ParentResults = null,
            CancellationToken = CancellationToken.None, IdempotencyKey = "test-key"
        };
    }

    private static RunRow RunRow(Guid refId) => new()
    {
        Id = 99, RefId = refId, WorkflowDefinitionId = 7, WorkflowRefId = ChildRefId, WorkflowVersion = 1,
        TriggerNodeId = Guid.NewGuid(), CorrelationKey = "manual:x", Status = "Running",
        StartedAt = DateTime.UtcNow, CompletedAt = null, CancellationRequestedAt = null, CancellationReason = null,
        CreditBudget = 0m, CreatedAt = DateTime.UtcNow
    };

    private sealed class FixedClock : IClock
    {
        public DateTime UtcNow => new(2026, 6, 6, 12, 0, 0, DateTimeKind.Utc);
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
