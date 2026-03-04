using Webskt.Workflow.Abstraction.Models.Expressions;
using Webskt.Workflow.Engine.Host.Interfaces;
using ExecutionContext = Webskt.Workflow.Engine.Host.Models.ExecutionContext;

namespace Webskt.Workflow.Engine.Host.Services.ExpressionEvaluators;

public sealed partial class WorkflowExpressionEvaluator : IWorkflowExpressionEvaluator
{
    private readonly ILogger<WorkflowExpressionEvaluator> _logger;

    public WorkflowExpressionEvaluator(ILogger<WorkflowExpressionEvaluator> logger)
    {
        _logger = logger;
    }

    public object? Evaluate(WorkflowExpression expression, ExecutionContext context)
    {
        try
        {
            return expression switch
            {
                LiteralExpression lit => EvaluateLiteral(lit),
                MemberAccessExpression acc => EvaluateAccess(acc, context),
                UnaryExpression un => EvaluateUnary(un, context),
                BinaryExpression bin => EvaluateBinary(bin, context),
                FunctionExpression fn => EvaluateFunction(fn, context),
                _ => null
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error evaluating expression of type {ExprType}", expression.GetType().Name);
            return null;
        }
    }

    private static bool AsBool(object? value) => value is true;
    
    private static double AsDouble(object? value)
    {
        try
        {
            return Convert.ToDouble(value ?? 0);
        }
        catch
        {
            return 0;
        }
    }
}
