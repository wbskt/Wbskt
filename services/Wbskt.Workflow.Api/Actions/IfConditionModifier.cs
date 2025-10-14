using System.Text.Json;
using Wbskt.Workflow.Api.Core;
using Wbskt.Workflow.Api.Core.Abstractions;

namespace Wbskt.Workflow.Api.Actions;

public class IfConditionModifier : IAction
{
    private readonly ILogger<IfConditionModifier> _logger;

    public IfConditionModifier(ILogger<IfConditionModifier> logger)
    {
        _logger = logger;
    }

    public Task<ActionResult> ExecuteAsync(WorkflowContext context, CancellationToken cancellationToken)
    {
        // For now, we'll use a simple hardcoded condition.
        // In a real implementation, this would parse the StepConfiguration.
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
