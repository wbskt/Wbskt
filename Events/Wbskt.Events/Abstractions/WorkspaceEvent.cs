using System.Text.Json.Serialization;
using Wbskt.EventBus.Abstractions;

namespace Wbskt.Events.Abstractions;

public abstract record WorkspaceEvent : BaseEvent
{
    protected WorkspaceEvent(int WorkspaceId)
    {
        this.WorkspaceId = WorkspaceId;
    }

    // [JsonIgnore] TODO: shouldn't pass this in signal r but must be serialised in rabbit mq
    public int WorkspaceId { get; }
}
