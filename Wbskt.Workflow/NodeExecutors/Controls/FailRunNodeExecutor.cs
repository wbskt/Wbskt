using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.NodeExecutors.Controls;

/// <summary>
/// Explicitly fails the run with a non-retryable error. FailResultHandler recognises the node and
/// fails the whole run - Failing, the remaining branches stopped, Failed at finalization - whether
/// or not the workflow is fail-fast.
/// </summary>
internal sealed class FailRunNodeExecutor : INodeExecutor
{
    public string Kind => NodeKind.ControlFailRun;

    public bool IsSideEffectFree => true;

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
