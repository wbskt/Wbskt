using Webskt.Workflow.Abstraction.Models;
using Webskt.Workflow.Abstraction.Models.Nodes;
using Webskt.Workflow.Abstraction.Models.Nodes.Controls;
using Webskt.Workflow.Engine.Host.Interfaces;
using Webskt.Workflow.Engine.Host.Models;
using ExecutionContext = Webskt.Workflow.Engine.Host.Models.ExecutionContext;

namespace Webskt.Workflow.Engine.Host.Services.NodeExecutors;

public sealed class VariableExecutor : IWorkflowNodeExecutor
{
    private readonly IWorkflowExpressionEvaluator _evaluator;

    public VariableExecutor(IWorkflowExpressionEvaluator evaluator)
    {
        _evaluator = evaluator;
    }

    public Task<NodeExecutionResult> ExecuteAsync(BaseNode node, ExecutionContext context)
    {
        var varNode = (VariableNode)node;
        var varName = varNode.VariableName;

        if (string.IsNullOrWhiteSpace(varName))
        {
            return Task.FromResult(NodeExecutionResult.Fail("Variable name is required."));
        }

        object? resultValue = null;

        switch (varNode.Operation.ToLowerInvariant())
        {
            case "set":
                if (varNode.Expression != null)
                {
                    resultValue = _evaluator.Evaluate(varNode.Expression, context);
                    context.SetState(varName, resultValue);
                }
                break;

            case "get":
                resultValue = context.GetState(varName);
                context.LastNodeOutput = resultValue;
                break;

            case "increment":
                var currentInc = Convert.ToDouble(context.GetState(varName) ?? 0);
                var incBy = varNode.Expression != null ? Convert.ToDouble(_evaluator.Evaluate(varNode.Expression, context) ?? 1) : 1;
                resultValue = currentInc + incBy;
                context.SetState(varName, resultValue);
                break;

            case "decrement":
                var currentDec = Convert.ToDouble(context.GetState(varName) ?? 0);
                var decBy = varNode.Expression != null ? Convert.ToDouble(_evaluator.Evaluate(varNode.Expression, context) ?? 1) : 1;
                resultValue = currentDec - decBy;
                context.SetState(varName, resultValue);
                break;
        }

        return Task.FromResult(NodeExecutionResult.Success(PortNames.Out));
    }
}
