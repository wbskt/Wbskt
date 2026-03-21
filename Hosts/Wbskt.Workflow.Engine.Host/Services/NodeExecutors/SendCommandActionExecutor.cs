using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Client;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
using Wbskt.Workflow.Engine.Host.Interfaces;
using Wbskt.Workflow.Engine.Host.Models;
using ExecutionContext = Wbskt.Workflow.Engine.Host.Models.ExecutionContext;

namespace Wbskt.Workflow.Engine.Host.Services.NodeExecutors;

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

        // In a real implementation, we might want to evaluate the Payload using a template engine
        // For MVP, we'll just send it as-is or do simple string replacements if needed.
        
        await _eventBus.PublishAsync(new ClientPayloadEvent(
            commandNode.TargetClientRefId,
            0, // TODO: we need actual client id. either we fetch it here or fill id in the node definition when workflow is saved
            context.WorkspaceId,
            commandNode.MessageType,
            commandNode.Payload
        ));

        return NodeExecutionResult.Success(PortNames.Out);
    }
}
