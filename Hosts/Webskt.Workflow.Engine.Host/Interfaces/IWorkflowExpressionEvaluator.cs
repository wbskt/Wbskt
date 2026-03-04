using Webskt.Workflow.Abstraction.Models.Expressions;
using ExecutionContext = Webskt.Workflow.Engine.Host.Models.ExecutionContext;

namespace Webskt.Workflow.Engine.Host.Interfaces;

public interface IWorkflowExpressionEvaluator
{
    /// <summary>
    /// Evaluates the given expression against the provided execution context.
    /// This is a synchronous, in-memory operation.
    /// </summary>
    object? Evaluate(WorkflowExpression expression, ExecutionContext context);
}
