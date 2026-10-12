using System.Text.Json;
using Wbskt.EventBus.Abstractions;
using Wbskt.Infrastructure.Json;

namespace Wbskt.Infrastructure.Events.Outbox;

/// <summary>
/// An event as the outbox stores it: its type's full name and its JSON. Written by the procedure that
/// makes the change, in the same transaction, and published later by <see cref="OutboxDispatcher"/>.
/// </summary>
public sealed record OutboxMessage(string MessageType, string Payload)
{
    internal static readonly JsonSerializerOptions JsonOptions = WbsktJsonExtensions.Apply(new JsonSerializerOptions(JsonSerializerDefaults.Web));

    /// <summary>
    /// The event ready to be written. Actor stamping does not happen here: the outbox is for events
    /// whose content is decided by the change itself, not by who is signed in when it is published.
    /// </summary>
    public static OutboxMessage Create<TEvent>(TEvent @event) where TEvent : IEvent
    {
        return new OutboxMessage(typeof(TEvent).FullName!, JsonSerializer.Serialize(@event, JsonOptions));
    }
}

/// <summary>An unsent row of the outbox.</summary>
public sealed record PendingOutboxMessage(long Id, string MessageType, string Payload, DateTime CreatedAt);
