using System.Text.Json.Serialization;
using Wbskt.EventBus.Abstractions;

namespace Wbskt.Events.Abstractions;

public abstract record WorkspaceEvent : BaseEvent
{
    protected WorkspaceEvent(int WorkspaceId)
    {
        this.WorkspaceId = WorkspaceId;
    }

    [JsonIgnore]
    public int WorkspaceId { get; }
}
