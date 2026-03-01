using Webskt.Workflow.Abstraction.Models;
using Webskt.Workflow.Abstraction.Models.Nodes;
using ExecutionContext = Webskt.Workflow.Abstraction.Models.ExecutionContext;

namespace Webskt.Workflow.Abstraction.Interfaces;

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
