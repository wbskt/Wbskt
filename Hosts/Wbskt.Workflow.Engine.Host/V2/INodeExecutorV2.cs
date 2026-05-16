using Wbskt.Workflow.Abstraction.Models.Nodes;

namespace Wbskt.Workflow.Engine.Host.V2;

/// <summary>
/// Defines the execution logic for one type of workflow node in the V2 runtime.
/// </summary>
public interface INodeExecutorV2
{
    /// <summary>Returns true when this executor is responsible for the given node.</summary>
    bool CanExecute(BaseNode node);

    /// <summary>Runs the node's logic against the branch context and returns the result.</summary>
    Task<NodeExecutionResultV2> ExecuteAsync(BaseNode node, BranchContext context, CancellationToken ct);
}

