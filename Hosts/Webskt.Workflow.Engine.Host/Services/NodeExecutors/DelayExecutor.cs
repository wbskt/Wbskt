using Webskt.Workflow.Abstraction.Models;
using Webskt.Workflow.Abstraction.Models.Nodes;
using Webskt.Workflow.Abstraction.Models.Nodes.Controls;
using Webskt.Workflow.Engine.Host.Interfaces;
using Webskt.Workflow.Engine.Host.Models;
using ExecutionContext = Webskt.Workflow.Engine.Host.Models.ExecutionContext;

namespace Webskt.Workflow.Engine.Host.Services.NodeExecutors;

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
