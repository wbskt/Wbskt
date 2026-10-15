using System.Text.Json.Serialization;

namespace Wbskt.Workflow.Abstraction.Models.Bookmarks;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(TimerWakeCondition), "timer")]
[JsonDerivedType(typeof(SignalWakeCondition), "signal")]
[JsonDerivedType(typeof(HttpWakeCondition), "http")]
[JsonDerivedType(typeof(ChildRunCompletedWakeCondition), "childRunCompleted")]
[JsonDerivedType(typeof(CommandOutcomeWakeCondition), "commandOutcome")]
public abstract record WakeCondition
{
    /// <summary>
    /// The bookmark row's WakeConditionKind: the same name as this subtype's JSON discriminator. Abstract
    /// so a new wake condition cannot be added without saying what kind of bookmark it parks on. Not
    /// serialized: the discriminator already carries it, and the stored JSON keeps its shape.
    /// </summary>
    [JsonIgnore]
    public abstract string Kind { get; }

    [JsonPropertyName("ttl")]
    public TimeSpan? Ttl { get; init; }

    [JsonPropertyName("ttlPort")]
    public string? TtlPort { get; init; }

    /// <summary>
    /// When the bookmark expires on its own, given the moment the branch parks: the TTL, if any. A timer
    /// overrides this, since its time is the wake itself.
    /// </summary>
    public virtual DateTime? ExpiresAt(DateTime nowUtc)
    {
        return Ttl is TimeSpan ttl ? nowUtc + ttl : null;
    }
}
