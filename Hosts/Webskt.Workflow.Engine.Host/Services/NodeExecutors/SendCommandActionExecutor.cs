using Webskt.EventBus.Abstractions;
using Webskt.Events.Client;
using Webskt.Workflow.Abstraction.Models;
using Webskt.Workflow.Abstraction.Models.Nodes;
using Webskt.Workflow.Abstraction.Models.Nodes.Actions;
using Webskt.Workflow.Engine.Host.Interfaces;
using Webskt.Workflow.Engine.Host.Models;
using ExecutionContext = Webskt.Workflow.Engine.Host.Models.ExecutionContext;

namespace Webskt.Workflow.Engine.Host.Services.NodeExecutors;

public sealed class SendCommandActionExecutor : IWorkflowNodeExecutor
{
    private readonly IEventBus _eventBus;

    public SendCommandActionExecutor(IEventBus eventBus)
    {
        _eventBus = eventBus;
    }

    public async Task<NodeExecutionResult> ExecuteAsync(BaseNode node, ExecutionContext context)
    {
        var commandNode = (SendCommandActionNode)node;

        // In a real implementation, we might want to evaluate the PayloadTemplate using a template engine
        // For MVP, we'll just send it as-is or do simple string replacements if needed.
        
        await _eventBus.PublishAsync(new ClientPayloadEvent(
            commandNode.TargetClientRefId,
            context.WorkspaceId,
            commandNode.ActionName,
            commandNode.PayloadTemplate
        ));

        return NodeExecutionResult.Success(PortNames.Out);
    }
}
