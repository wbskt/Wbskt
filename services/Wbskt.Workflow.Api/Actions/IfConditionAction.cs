using System.Text.Json;
using Wbskt.Common.Configurations;
using Wbskt.Workflow.Api.Core;
using Wbskt.Workflow.Api.Core.Abstractions;

namespace Wbskt.Workflow.Api.Actions;

public class IfConditionAction : IAction
{
    private readonly ILogger<IfConditionAction> _logger;

    public IfConditionAction(ILogger<IfConditionAction> logger)
    {
        _logger = logger;
    }

    public Task<ActionResult> ExecuteAsync(StepConfigurationBase configuration, WorkflowContext context, CancellationToken cancellationToken)
    {
        var config = configuration as IfConditionConfiguration;
        // In a real implementation, this would use a proper expression evaluator.
        if (context.Properties.TryGetValue("temperature", out var tempValue) && tempValue is JsonElement tempElement && tempElement.TryGetInt32(out var temperature))
        {
            if (temperature > 40)
            {
                _logger.LogInformation("IfCondition: Temperature ({Temperature}) is greater than 40. Result: true.", temperature);
                return Task.FromResult(new ActionResult(true));
            }
        }

        _logger.LogInformation("IfCondition: Condition not met. Result: false.");
        return Task.FromResult(new ActionResult(false));
    }
}
