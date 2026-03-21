using Wbskt.EventBus.Abstractions;

namespace Wbskt.Events.Abstractions;

public interface IWorkspaceContext : IEvent
{
    [SignalRPrivate] int WorkspaceId { get; }
}