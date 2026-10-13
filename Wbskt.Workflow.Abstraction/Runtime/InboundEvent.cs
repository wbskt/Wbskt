using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wbskt.Workflow.Abstraction.Runtime;

public sealed record InboundEvent(
    string ChannelKind,
    IReadOnlyCollection<string> MatchKeys,
    string InboundEventId,
    IReadOnlyDictionary<string, JsonElement> Payload,
    DateTime ReceivedAt)
{
    public string? CorrelationKey { get; init; }

    /// <summary>
    /// The hash (<see cref="WebhookSecretHash"/>) of the shared secret the caller presented, for channels
    /// that have one (today: webhook). Deliberately kept out of <see cref="Payload"/> - the payload is
    /// persisted as the run's trigger data and rendered in the history trace, and a credential has no
    /// business in either.
    /// </summary>
    public string? SecretHash { get; init; }

    /// <summary>
    /// Set only on a queued event replayed when its correlation slot frees up: the trigger that queued
    /// it. The original arrival already reached every other registration and any bookmark it matched, so
    /// the replay goes to this trigger alone.
    /// </summary>
    public QueuedTrigger? ReplayFor { get; init; }

    /// <summary>
    /// Set only by a parent's sub-workflow node starting its child. The parent already holds a place
    /// under the workspace's active-run ceiling, so the child is started past it. Never read from
    /// JSON, so no inbound adapter (or a queued event's stored copy) can claim it.
    /// </summary>
    [JsonIgnore]
    public bool StartsChildRun { get; init; }
}

/// <summary>The workflow trigger a queued event is waiting for.</summary>
public sealed record QueuedTrigger(Guid WorkflowRefId, Guid TriggerNodeId);
