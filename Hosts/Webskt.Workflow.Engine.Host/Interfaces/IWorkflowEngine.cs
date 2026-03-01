using Webskt.Workflow.Abstraction.Models;
using Webskt.Workflow.Engine.Host.Models;

namespace Webskt.Workflow.Engine.Host.Interfaces;

public interface IWorkflowEngine
{
    /// <summary>
    /// Starts a new workflow instance from a definition and trigger data.
    /// </summary>
    Task<WorkflowInstance> StartAsync(WorkflowDefinition definition, object? triggerData);

    /// <summary>
    /// Resumes an existing workflow instance (e.g., after a delay).
    /// </summary>
    Task ResumeAsync(Guid instanceId);
}
