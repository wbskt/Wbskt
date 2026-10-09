using Wbskt.Events;
using Wbskt.Events.Abstractions;
using Wbskt.Events.Auth;
using Wbskt.Events.Client;
using Wbskt.Events.Management;
using Wbskt.Events.Workflow;

namespace Wbskt.Management.Host.Services;

/// <summary>
/// Where a logged event came from. An action a person or a run took is stamped when it is published
/// (see <c>ActorStampingEventBus</c>); everything else is known from what kind of event it is.
/// </summary>
public static class EventSources
{
    private static readonly IReadOnlyDictionary<string, Type> Events = typeof(DeviceTrafficAttribute).Assembly.GetTypes()
        .Where(t => t is { IsClass: true, IsAbstract: false, IsNested: false } && typeof(EventBus.Abstractions.BaseEvent).IsAssignableFrom(t))
        .ToDictionary(t => t.Name, StringComparer.Ordinal);

    /// <summary>
    /// The source of an entry of <paramref name="eventName"/>: <paramref name="stamped"/> when the
    /// publisher named one; else, for an action event, the API when a user is named and the system
    /// when not; else from the event's kind. Null for a sign-in or permission event, which the auth
    /// host does not stamp yet, and for an event this build does not know.
    /// </summary>
    public static EventSource? Derive(string eventName, EventSource? stamped, bool hasUser)
    {
        if (stamped is not null)
        {
            return stamped;
        }

        if (!Events.TryGetValue(eventName, out var type))
        {
            return null;
        }

        if (typeof(ActorEvent).IsAssignableFrom(type))
        {
            return hasUser ? EventSource.Api : EventSource.System;
        }

        if (type.IsDefined(typeof(DeviceTrafficAttribute), inherit: false)
            || type.Namespace == typeof(ClientConnectedEvent).Namespace
            // A device joining through a policy, and the joins a policy turned away.
            || (type.Namespace == typeof(PolicyCreatedEvent).Namespace
                && (type.Name.StartsWith("Client", StringComparison.Ordinal) || type.Name.StartsWith("PolicyRegistration", StringComparison.Ordinal))))
        {
            return EventSource.Device;
        }

        if (type.Namespace == typeof(WorkflowRunStartedEvent).Namespace)
        {
            return EventSource.Workflow;
        }

        return type.Namespace == typeof(UserLoginSuccessEvent).Namespace ? null : EventSource.System;
    }
}
