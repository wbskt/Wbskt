using System.Collections;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
using Wbskt.Workflow.Engine.Host.Interfaces;
using Wbskt.Workflow.Engine.Host.Models;
using ExecutionContext = Wbskt.Workflow.Engine.Host.Models.ExecutionContext;

namespace Wbskt.Workflow.Engine.Host.Services.NodeExecutors;

public sealed class LoopExecutor : IWorkflowNodeExecutor
{
    private readonly IWorkflowExpressionEvaluator _evaluator;

    public LoopExecutor(IWorkflowExpressionEvaluator evaluator)
    {
        _evaluator = evaluator;
    }

    public Task<NodeExecutionResult> ExecuteAsync(BaseNode node, ExecutionContext context)
    {
        var loopNode = (LoopNode)node;
        
        // 1. Get the list of items
        var itemsResult = loopNode.ItemsExpression != null 
            ? _evaluator.Evaluate(loopNode.ItemsExpression, context) 
            : null;

        if (itemsResult is not IEnumerable items)
        {
            return Task.FromResult(NodeExecutionResult.Success(PortNames.Completed));
        }

        var itemList = items.Cast<object>().ToList();
        
        // 2. Manage the loop index in the shared instance state
        var indexKey = $"$loop_{node.NodeId}_index";
        var currentIndex = Convert.ToInt32(context.GetState(indexKey) ?? 0);

        if (currentIndex < itemList.Count)
        {
            // 3. Set the current item variable
            context.SetState(loopNode.IteratorName, itemList[currentIndex]);
            
            // 4. Increment index for the NEXT time this node is visited
            context.SetState(indexKey, currentIndex + 1);

            return Task.FromResult(NodeExecutionResult.Success(PortNames.Body));
        }

        // 5. Loop finished, clean up state
        context.SetState(indexKey, null);
        context.SetState(loopNode.IteratorName, null);

        return Task.FromResult(NodeExecutionResult.Success(PortNames.Completed));
    }
}
