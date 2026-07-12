using System.Text.Json;
using Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.NodeExecutors.Controls;

namespace Wbskt.Workflow.Engine.Host.Tests.NodeExecutors.Controls;

public sealed class EndAndFailRunNodeExecutorTests
{
    [Fact]
    public async Task End_completes_the_branch()
    {
        var executor = new EndNodeExecutor();
        var node = new EndNode { NodeId = Guid.NewGuid(), Name = "end", Ports = [] };
        NodeContext ctx = Context(node);

        NodeExecutionResult result = await executor.ExecuteAsync(ctx, CancellationToken.None);

        var terminal = Assert.IsType<NodeExecutionResult.Terminal>(result);
        Assert.Equal(BranchTerminalReason.Completed, terminal.Reason);
    }

    [Fact]
    public async Task FailRun_fails_branch_with_configured_reason_non_retryable()
    {
        var executor = new FailRunNodeExecutor();
        var node = new FailRunNode { NodeId = Guid.NewGuid(), Name = "fail", Ports = [], Config = new FailRunConfig { Reason = "temperature too high" } };
        NodeContext ctx = Context(node);

        NodeExecutionResult result = await executor.ExecuteAsync(ctx, CancellationToken.None);

        var fail = Assert.IsType<NodeExecutionResult.Fail>(result);
        Assert.Equal("FAIL_RUN", fail.ErrorCode);
        Assert.Equal("temperature too high", fail.Message);
        Assert.False(fail.Retryable);
    }

    [Fact]
    public async Task FailRun_uses_default_reason_when_config_missing()
    {
        var executor = new FailRunNodeExecutor();
        var node = new FailRunNode { NodeId = Guid.NewGuid(), Name = "fail", Ports = [], Config = null };
        NodeContext ctx = Context(node);

        var fail = Assert.IsType<NodeExecutionResult.Fail>(await executor.ExecuteAsync(ctx, CancellationToken.None));
        Assert.Equal("FAIL_RUN", fail.ErrorCode);
        Assert.False(string.IsNullOrWhiteSpace(fail.Message));
    }

    private static NodeContext Context(Wbskt.Workflow.Abstraction.Models.Nodes.BaseNode node)
    {
        return new NodeContext
        {
            Branch = new BranchContext(42, 1001, 5, Guid.NewGuid(), 1, node.NodeId.ToString(), 1, new Dictionary<string, JsonElement>(), new Dictionary<string, JsonElement>(), "corr-1", DateTime.UtcNow, 9),
            Node = node,
            Providers = new StubProviderComposite(),
            Tick = 1,
            ParentResults = null,
            CancellationToken = CancellationToken.None, IdempotencyKey = "test-key"
        };
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
