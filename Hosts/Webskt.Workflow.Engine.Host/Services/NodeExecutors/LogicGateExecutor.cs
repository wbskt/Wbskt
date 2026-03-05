using Webskt.Workflow.Abstraction.Models;
using Webskt.Workflow.Abstraction.Models.Nodes;
using Webskt.Workflow.Abstraction.Models.Nodes.Controls;
using Webskt.Workflow.Engine.Host.Interfaces;
using Webskt.Workflow.Engine.Host.Models;
using ExecutionContext = Webskt.Workflow.Engine.Host.Models.ExecutionContext;

namespace Webskt.Workflow.Engine.Host.Services.NodeExecutors;

public sealed class LogicGateExecutor : IWorkflowNodeExecutor
{
    private readonly IWorkflowExpressionEvaluator _evaluator;

    public LogicGateExecutor(IWorkflowExpressionEvaluator evaluator)
    {
        _evaluator = evaluator;
    }

    public Task<NodeExecutionResult> ExecuteAsync(BaseNode node, ExecutionContext context)
    {
        var logicNode = (LogicGateNode)node;

        var result = _evaluator.Evaluate(logicNode.Condition, context);

        var isMatch = result is bool b && b;

        var nextPort = isMatch ? PortNames.Match : PortNames.Otherwise;

        return Task.FromResult(NodeExecutionResult.Success(nextPort));
    }
}
