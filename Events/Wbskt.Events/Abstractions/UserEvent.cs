using System.Text.Json.Serialization;

namespace Wbskt.Events.Abstractions;

public abstract record UserEvent : AuthEvent
{
    protected UserEvent(int UserId)
    {
        this.UserId = UserId;
    }

    // [JsonIgnore] TODO: shouldn't pass this in signal r but must be serialised in rabbit mq
    public int UserId { get; }
}