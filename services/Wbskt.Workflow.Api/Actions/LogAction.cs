using Wbskt.Workflow.Api.Core;
using Wbskt.Workflow.Api.Core.Abstractions;

namespace Wbskt.Workflow.Api.Actions;

public class LogAction : IAction
{
    private readonly ILogger<LogAction> _logger;
    public LogAction(ILogger<LogAction> logger)
    {
        _logger = logger;
    }

    public Task<ActionResult> ExecuteAsync(WorkflowContext context, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing LogAction for WorkflowExecutionId: {Id}", context.WorkflowExecutionId);

        foreach (var prop in context.Properties)
        {
            _logger.LogInformation("Context Key: {Key}, Value: {Value}", prop.Key, prop.Value);
        }

        return Task.FromResult(new ActionResult(true));
    }
}
