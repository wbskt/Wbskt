using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
using Wbskt.Workflow.Engine.Host.Interfaces;
using Wbskt.Workflow.Engine.Host.Models;
using ExecutionContext = Wbskt.Workflow.Engine.Host.Models.ExecutionContext;

namespace Wbskt.Workflow.Engine.Host.Services.NodeExecutors;

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
