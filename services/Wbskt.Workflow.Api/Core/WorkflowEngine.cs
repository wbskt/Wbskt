using Wbskt.Workflow.Api.Core.Abstractions;

namespace Wbskt.Workflow.Api.Core;

public class WorkflowEngine : IWorkflowEngine
{
    private readonly ILogger<WorkflowEngine> _logger;

    // Inject dependencies later (e.g., logger, service provider)
    public WorkflowEngine(ILogger<WorkflowEngine> logger)
    {
        _logger = logger;
    }

    public Task ExecuteWorkflowAsync(Guid workflowRefId, WorkflowContext initialContext)
    {
        // In Phase 2, we will implement the logic here.
        // For now, this confirms the service can be called.
        _logger.LogInformation("Pretending to execute workflow {WorkflowRefId}.", workflowRefId);
        return Task.CompletedTask;
    }
}
