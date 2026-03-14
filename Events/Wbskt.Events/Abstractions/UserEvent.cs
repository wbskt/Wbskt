using System.Text.Json.Serialization;

namespace Wbskt.Events.Abstractions;

public abstract record UserEvent : AuthEvent
{
    protected UserEvent(int UserId)
    {
        this.UserId = UserId;
    }

    [JsonIgnore]
    public int UserId { get; }
}