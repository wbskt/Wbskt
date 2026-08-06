using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Workflow;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Engine.Host.InboundAdapters;

/// <summary>
/// Publishes <see cref="WorkflowToastEvent"/> onto the bus. The management host's
/// SignalRForwardingHandler picks it up automatically (the event carries [SignalRNotify] and
/// IWorkspaceContext) and broadcasts it to that workspace's notification group - so the engine host
/// needs no SignalR dependency of its own.
/// </summary>
public sealed class ToastPublisher(IEventBus eventBus) : IToastPublisher
{
    public Task PublishToastAsync(
        Guid workflowRefId,
        int workflowDefinitionId,
        Guid runRefId,
        int workspaceId,
        string title,
        string message,
        CancellationToken ct)
    {
        return eventBus.PublishAsync(
            new WorkflowToastEvent(workflowRefId, workflowDefinitionId, runRefId, workspaceId, title, message),
            ct);
    }
}
