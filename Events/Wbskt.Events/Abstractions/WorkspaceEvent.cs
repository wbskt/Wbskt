using System.Text.Json.Serialization;
using Wbskt.Common.Abstraction;
using Wbskt.EventBus.Abstractions;

namespace Wbskt.Events.Abstractions;

public abstract record WorkspaceEvent : BaseEvent
{
    protected WorkspaceEvent(int WorkspaceId)
    {
        this.WorkspaceId = WorkspaceId;
    }

    [SignalRPrivate]
    public int WorkspaceId { get; }
}
