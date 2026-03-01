using Webskt.Workflow.Abstraction.Models.Expressions;
using ExecutionContext = Webskt.Workflow.Engine.Host.Models.ExecutionContext;

namespace Webskt.Workflow.Engine.Host.Interfaces;

/// <summary>
/// Defines the logic for evaluating workflow expressions.
/// </summary>
public interface IWorkflowExpressionEvaluator
{
    /// <summary>
    /// Evaluates the given expression against the provided execution context.
    /// </summary>
    Task<object?> EvaluateAsync(WorkflowExpression expression, ExecutionContext context);
}
