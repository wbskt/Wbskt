using Wbskt.Workflow.Abstraction.Models;

namespace Wbskt.Workflow.Engine.Host.Interfaces;

/// <summary>
/// Maintains an in-memory high-performance index of active workflows by their trigger keys.
/// </summary>
public interface IWorkflowRuntimeRegistry
{
    /// <summary>
    /// Retrieves all enabled workflows associated with a specific trigger key (e.g. "client:guid").
    /// </summary>
    IReadOnlyCollection<WorkflowDefinition> GetWorkflows(string triggerKey);

    /// <summary>
    /// Adds or updates a workflow in the runtime index.
    /// </summary>
    void RegisterWorkflow(WorkflowDefinition definition);

    /// <summary>
    /// Removes a workflow from the runtime index.
    /// </summary>
    void UnregisterWorkflow(Guid workflowRefId);

    /// <summary>
    /// Clears and reloads the entire registry (usually called at startup).
    /// </summary>
    void Initialize(IEnumerable<WorkflowDefinition> definitions);
}
