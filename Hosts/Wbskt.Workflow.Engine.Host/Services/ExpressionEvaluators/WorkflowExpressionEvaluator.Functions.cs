using System.Collections;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models.Expressions;
using ExecutionContext = Wbskt.Workflow.Engine.Host.Models.ExecutionContext;

namespace Wbskt.Workflow.Engine.Host.Services.ExpressionEvaluators;

public sealed partial class WorkflowExpressionEvaluator
{
    private object? EvaluateFunction(FunctionExpression expression, ExecutionContext context)
    {
        var args = expression.Arguments.Select(a => Evaluate(a, context)).ToList();

        return expression.Function switch
        {
            // Date/Time
            FunctionName.Now => DateTime.Now,
            FunctionName.UtcNow => DateTime.UtcNow,
            FunctionName.AddDays => args.Count >= 2 ? ((DateTime)args[0]!).AddDays(AsDouble(args[1])) : null,

            // Math
            FunctionName.Round => Math.Round(AsDouble(args.FirstOrDefault())),
            FunctionName.Floor => Math.Floor(AsDouble(args.FirstOrDefault())),
            FunctionName.Ceiling => Math.Ceiling(AsDouble(args.FirstOrDefault())),
            FunctionName.Abs => Math.Abs(AsDouble(args.FirstOrDefault())),
            FunctionName.Min => args.Select(AsDouble).Min(),
            FunctionName.Max => args.Select(AsDouble).Max(),

            // String
            FunctionName.Upper => args.FirstOrDefault()?.ToString()?.ToUpperInvariant(),
            FunctionName.Lower => args.FirstOrDefault()?.ToString()?.ToLowerInvariant(),
            FunctionName.Trim => args.FirstOrDefault()?.ToString()?.Trim(),
            FunctionName.Concat => string.Concat(args.Select(a => a?.ToString() ?? string.Empty)),
            FunctionName.Contains => args.Count >= 2 && (args[0]?.ToString()?.Contains(args[1]?.ToString() ?? string.Empty) ?? false),

            // Collection
            FunctionName.Count => (args.FirstOrDefault() as IEnumerable)?.Cast<object>().Count() ?? 0,
            FunctionName.First => (args.FirstOrDefault() as IEnumerable)?.Cast<object>().FirstOrDefault(),
            FunctionName.Last => (args.FirstOrDefault() as IEnumerable)?.Cast<object>().LastOrDefault(),

            // Logic
            FunctionName.Coalesce => args.FirstOrDefault(a => a != null),

            _ => null
        };
    }
}
