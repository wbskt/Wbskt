using Wbskt.Workflow.Abstraction.Models;

namespace Wbskt.Workflow.Abstraction.Runtime;

public interface IWorkflowDefinitionCache
{
    Task<WorkflowDefinition> GetAsync(int workflowDefinitionId, CancellationToken ct);

    void Invalidate(int workflowDefinitionId);
}
