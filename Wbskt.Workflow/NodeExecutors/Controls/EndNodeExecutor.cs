using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.NodeExecutors.Controls;

/// <summary>
/// Explicit terminal node. Completes the branch; when the run's last active branch completes,
/// the run finalizes (Succeeded unless other branches failed).
/// </summary>
internal sealed class EndNodeExecutor : INodeExecutor
{
    public string Kind => NodeKind.ControlEnd;

    public Task<NodeExecutionResult> ExecuteAsync(NodeContext ctx, CancellationToken ct)
    {
        _ = ctx;
        _ = ct;
        return Task.FromResult<NodeExecutionResult>(new NodeExecutionResult.Terminal(BranchTerminalReason.Completed));
    }
}
