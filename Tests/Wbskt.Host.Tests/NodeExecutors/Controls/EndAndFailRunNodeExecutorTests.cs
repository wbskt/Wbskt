using System.Text.Json;
using Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Abstraction.Models.Expressions;
using Wbskt.Workflow.NodeExecutors.Controls;
using Wbskt.Workflow.Runtime;

namespace Wbskt.Host.Tests.NodeExecutors.Controls;

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
    public async Task End_with_no_output_carries_none()
    {
        var executor = new EndNodeExecutor(new ExpressionEvaluator(TimeProvider.System));
        var node = new EndNode { NodeId = Guid.NewGuid(), Name = "end", Ports = [], Config = new EndConfig() };

        var terminal = Assert.IsType<NodeExecutionResult.Terminal>(await executor.ExecuteAsync(Context(node), CancellationToken.None));
        Assert.Null(terminal.Output);
    }

    [Fact]
    public async Task End_evaluates_its_output_against_the_branch_and_carries_it_on_the_terminal_result()
    {
        // Evaluated here, where the branch's local state is at hand: by the time the run finalizes,
        // the branch that reached this node is gone.
        var executor = new EndNodeExecutor(new ExpressionEvaluator(TimeProvider.System));
        var node = new EndNode { NodeId = Guid.NewGuid(), Name = "end", Ports = [], Config = new EndConfig { Output = new BranchStateRefExpression("total") } };
        NodeContext ctx = Context(node, new Dictionary<string, JsonElement> { ["total"] = JsonSerializer.SerializeToElement(42) });

        var terminal = Assert.IsType<NodeExecutionResult.Terminal>(await executor.ExecuteAsync(ctx, CancellationToken.None));
        Assert.Equal(BranchTerminalReason.Completed, terminal.Reason);
        Assert.Equal(42, terminal.Output!.Value.GetInt32());
    }

    [Fact]
    public async Task End_fails_the_branch_when_its_output_cannot_be_evaluated()
    {
        var executor = new EndNodeExecutor(new ExpressionEvaluator(TimeProvider.System));
        var node = new EndNode { NodeId = Guid.NewGuid(), Name = "end", Ports = [], Config = new EndConfig { Output = new JsonPathExpression(new LiteralExpression("{}"), "$[[") } };

        var fail = Assert.IsType<NodeExecutionResult.Fail>(await executor.ExecuteAsync(Context(node), CancellationToken.None));
        Assert.Equal("END_OUTPUT_EVALUATION_ERROR", fail.ErrorCode);
        Assert.False(fail.Retryable);
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

    private static NodeContext Context(Wbskt.Workflow.Abstraction.Models.Nodes.BaseNode node, Dictionary<string, JsonElement>? localState = null)
    {
        return new NodeContext
        {
            Branch = new BranchContext(42, 1001, 5, Guid.NewGuid(), 1, node.NodeId.ToString(), 1, localState ?? new Dictionary<string, JsonElement>(), new Dictionary<string, JsonElement>(), "corr-1", DateTime.UtcNow, 9),
            Node = node,
            CancellationToken = CancellationToken.None, IdempotencyKey = "test-key"
        };
    }

}
