using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Engine.Host.Models;
using Wbskt.Workflow.Engine.Host.Models.TriggerContexts;

namespace Wbskt.Workflow.Engine.Host.Interfaces;

public interface IWorkflowEngine
{
    /// <summary>
    /// Starts a new workflow instance from a definition and typed trigger context.
    /// </summary>
    Task<WorkflowInstance> StartAsync(WorkflowDefinition definition, BaseTriggerContext triggerContext);

    /// <summary>
    /// Resumes an existing workflow instance (e.g., after a delay).
    /// </summary>
    Task ResumeAsync(Guid instanceId);
}
