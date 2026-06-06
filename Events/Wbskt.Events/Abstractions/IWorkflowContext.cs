using Wbskt.EventBus.Abstractions;

namespace Wbskt.Events.Abstractions;

public interface IWorkflowContext : IWorkspaceContext
{
    [SignalRPrivate] int WorkflowId { get; }
    Guid WorkflowRefId { get; }
}
