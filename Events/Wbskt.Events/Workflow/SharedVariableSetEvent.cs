using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;

namespace Wbskt.Events.Workflow;

/// <summary>
/// A person set a workflow's shared variable through the API. <see cref="ActorEvent.Changes"/> holds
/// the value before and after, each cut to <see cref="SharedVariableValues.MaxLength"/> characters.
/// </summary>
[EventCriticality(EventCriticality.Info)]
public sealed record SharedVariableSetEvent(Guid WorkflowRefId, int WorkspaceId, string Name)
    : ActorEvent, IWorkspaceContext;

/// <summary>
/// A workflow run wrote a shared variable (set, increment, decrement or a compare-and-set that won).
/// The run is the actor. Runs write at the rate they fire, so this is kept and hidden like device
/// traffic rather than as part of the audit trail.
/// </summary>
[DeviceTraffic]
[EventCriticality(EventCriticality.Info)]
public sealed record SharedVariableSetByRunEvent(Guid WorkflowRefId, int WorkflowId, int WorkspaceId, string Name, string Operation, string? Value)
    : ActorEvent, IWorkflowContext;

public static class SharedVariableValues
{
    /// <summary>How much of a value an event carries; a variable can hold a large JSON document.</summary>
    public const int MaxLength = 256;

    public static string? Cut(string? value) => value is { Length: > MaxLength } ? value[..MaxLength] + "…" : value;
}
