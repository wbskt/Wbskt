using Wbskt.Common.Configurations;
using Wbskt.Common.Enums;
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

    public Task<ActionResult> ExecuteAsync(StepConfigurationBase config, WorkflowContext context, CancellationToken cancellationToken)
    {
        try
        {
            var ifConfig = config as IfConditionConfiguration;
            var result = EvaluateConditionGroup(ifConfig!, context);
            _logger.LogInformation("IfCondition evaluation result: {Result}", result);
            return Task.FromResult(new ActionResult(result));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error evaluating IfCondition group.");
            return Task.FromResult(new ActionResult(false, ex.Message));
        }
    }

    private bool EvaluateConditionGroup(IfConditionConfiguration group, WorkflowContext context)
    {
        var results = new List<bool>();
        foreach (var conditionGroup in group.Conditions)
        {
            if (conditionGroup.Condition != null)
            {
                results.Add(EvaluateSimpleCondition(conditionGroup.Condition, context));
            }
            else if (conditionGroup.NestedGroup != null)
            {
                results.Add(EvaluateConditionGroup(conditionGroup.NestedGroup, context)); // Recursion
            }
        }

        if (group.LogicalOperator == LogicalOperatorType.And)
        {
            return results.All(r => r);
        }
        else // Or
        {
            return results.Any(r => r);
        }
    }

    private bool EvaluateSimpleCondition(SimpleCondition condition, WorkflowContext context)
    {
        // A real implementation would use a proper templating engine to resolve the LeftOperand
        var leftValue = context.Properties.TryGetValue(condition.LeftOperand, out var val) ? val.ToString() : string.Empty;
        var rightValue = condition.RightOperand;

        switch (condition.ComparisonType)
        {
            case DataType.Number:
                var leftNum = decimal.Parse(leftValue);
                var rightNum = decimal.Parse(rightValue);
                return condition.Operator switch
                {
                    OperatorType.Equals => leftNum == rightNum,
                    OperatorType.NotEquals => leftNum != rightNum,
                    OperatorType.GreaterThan => leftNum > rightNum,
                    OperatorType.LessThan => leftNum < rightNum,
                    OperatorType.GreaterThanOrEqual => leftNum >= rightNum,
                    OperatorType.LessThanOrEqual => leftNum <= rightNum,
                    _ => throw new NotSupportedException($"Operator {condition.Operator} not supported for Number type.")
                };

            case DataType.String:
            default:
                return condition.Operator switch
                {
                    OperatorType.Equals => leftValue.Equals(rightValue, StringComparison.OrdinalIgnoreCase),
                    OperatorType.NotEquals => !leftValue.Equals(rightValue, StringComparison.OrdinalIgnoreCase),
                    OperatorType.Contains => leftValue.Contains(rightValue, StringComparison.OrdinalIgnoreCase),
                    OperatorType.StartsWith => leftValue.StartsWith(rightValue, StringComparison.OrdinalIgnoreCase),
                    OperatorType.EndsWith => leftValue.EndsWith(rightValue, StringComparison.OrdinalIgnoreCase),
                    _ => throw new NotSupportedException($"Operator {condition.Operator} not supported for String type.")
                };
        }
    }
}