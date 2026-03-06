using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Engine.Host.Models;
using ExecutionContext = Wbskt.Workflow.Engine.Host.Models.ExecutionContext;

namespace Wbskt.Workflow.Engine.Host.Interfaces;

/// <summary>
/// Defines the logic for executing a specific type of workflow node.
/// </summary>
public interface IWorkflowNodeExecutor
{
    /// <summary>
    /// Executes the logic for the given node within the provided context.
    /// </summary>
    Task<NodeExecutionResult> ExecuteAsync(BaseNode node, ExecutionContext context);
}

