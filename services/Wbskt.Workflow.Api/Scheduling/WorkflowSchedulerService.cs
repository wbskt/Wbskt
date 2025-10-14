using NCrontab.Advanced;
using Wbskt.Common.Readers;
using Wbskt.Workflow.Api.Core;
using Wbskt.Workflow.Api.Core.Abstractions;

namespace Wbskt.Workflow.Api.Scheduling;

public class WorkflowSchedulerService
{
    private readonly IWorkflowsReader _workflowsReader;
    private readonly IWorkflowEngine _workflowEngine;
    private readonly ILogger<WorkflowSchedulerService> _logger;

    public WorkflowSchedulerService(
        IWorkflowsReader workflowsReader,
        IWorkflowEngine workflowEngine,
        ILogger<WorkflowSchedulerService> logger)
    {
        _workflowsReader = workflowsReader;
        _workflowEngine = workflowEngine;
        _logger = logger;
    }

    public async Task TriggerDueWorkflows()
    {
        _logger.LogInformation("Checking for due timed workflows.");
        var timedWorkflows = await _workflowsReader.GetActiveWorkflowsByTriggerTypeAsync("Timed", CancellationToken.None);

        foreach (var workflow in timedWorkflows)
        {
            if (string.IsNullOrWhiteSpace(workflow.TriggerConfiguration))
            {
                continue;
            }

            try
            {
                var cron = CrontabSchedule.Parse(workflow.TriggerConfiguration);
                var nextOccurrence = cron.GetNextOccurrence(DateTime.UtcNow.AddMinutes(-1));

                if (nextOccurrence <= DateTime.UtcNow)
                {
                    _logger.LogInformation("Triggering timed workflow {WorkflowRefId}", workflow.RefId);
                    var context = new WorkflowContext(Guid.NewGuid(), new Dictionary<string, object>());
                    _ = _workflowEngine.ExecuteWorkflowAsync(workflow.RefId, context);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process timed workflow {WorkflowRefId}", workflow.RefId);
            }
        }
    }
}
