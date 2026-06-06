using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.NodeExecutors.Controls;

/// <summary>
/// Explicitly fails the branch with a non-retryable error. On a fail-fast workflow this escalates
/// the run to Failing (cancelling the remaining branches); otherwise the run aggregates to Failed
/// or PartiallyFailed at finalization.
/// </summary>
public sealed class FailRunNodeExecutor : INodeExecutor
{
    public string Kind => NodeKind.ControlFailRun;

    public Task<NodeExecutionResult> ExecuteAsync(NodeContext ctx, CancellationToken ct)
    {
        _ = ct;
        var node = (FailRunNode)ctx.Node;
        string reason = string.IsNullOrWhiteSpace(node.Config?.Reason)
            ? "Run failed by a FailRun node."
            : node.Config!.Reason!;

        return Task.FromResult<NodeExecutionResult>(new NodeExecutionResult.Fail("FAIL_RUN", reason, false, null));
    }
}
