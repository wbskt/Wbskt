namespace Wbskt.Workflow.Api.Core.Abstractions;

public interface IWorkflowEngine
{
    Task ExecuteWorkflowAsync(Guid workflowRefId, WorkflowContext initialContext);
}
