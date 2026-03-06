using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
using Wbskt.Workflow.Engine.Host.Interfaces;
using Wbskt.Workflow.Engine.Host.Models;
using ExecutionContext = Wbskt.Workflow.Engine.Host.Models.ExecutionContext;

namespace Wbskt.Workflow.Engine.Host.Services.NodeExecutors;

public sealed class DelayExecutor : IWorkflowNodeExecutor
{
    public Task<NodeExecutionResult> ExecuteAsync(BaseNode node, ExecutionContext context)
    {
        var delayNode = (DelayNode)node;

        if (delayNode.Seconds <= 0)
        {
            return Task.FromResult(NodeExecutionResult.Success(PortNames.Out));
        }

        var resumeAt = DateTime.UtcNow.AddSeconds(delayNode.Seconds);

        return Task.FromResult(NodeExecutionResult.Wait(resumeAt));
    }
}
