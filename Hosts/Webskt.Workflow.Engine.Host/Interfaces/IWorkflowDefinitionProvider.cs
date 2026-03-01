using Webskt.Workflow.Abstraction.Models;

namespace Webskt.Workflow.Engine.Host.Interfaces;

public interface IWorkflowDefinitionProvider
{
    /// <summary>
    /// Fetches all enabled workflow definitions from the database.
    /// </summary>
    Task<IReadOnlyCollection<WorkflowDefinition>> GetAllEnabledAsync();

    /// <summary>
    /// Fetches a specific workflow definition by its public RefId.
    /// </summary>
    Task<WorkflowDefinition> GetByRefIdAsync(Guid workflowRefId);
}
