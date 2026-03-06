using Wbskt.Workflow.Abstraction.Models.Expressions;
using ExecutionContext = Wbskt.Workflow.Engine.Host.Models.ExecutionContext;

namespace Wbskt.Workflow.Engine.Host.Interfaces;

public interface IWorkflowExpressionEvaluator
{
    /// <summary>
    /// Evaluates the given expression against the provided execution context.
    /// This is a synchronous, in-memory operation.
    /// </summary>
    object? Evaluate(WorkflowExpression expression, ExecutionContext context);
}
