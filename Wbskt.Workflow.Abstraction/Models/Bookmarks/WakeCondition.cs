using System.Text.Json.Serialization;

namespace Wbskt.Workflow.Abstraction.Models.Bookmarks;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(TimerWakeCondition), "timer")]
[JsonDerivedType(typeof(SignalWakeCondition), "signal")]
[JsonDerivedType(typeof(InboundWakeCondition), "inbound")]
[JsonDerivedType(typeof(HttpWakeCondition), "http")]
[JsonDerivedType(typeof(ChildRunCompletedWakeCondition), "childRunCompleted")]
[JsonDerivedType(typeof(AnyOfWakeCondition), "anyOf")]
public abstract record WakeCondition
{
    [JsonPropertyName("ttl")]
    public TimeSpan? Ttl { get; init; }

    [JsonPropertyName("ttlPort")]
    public string? TtlPort { get; init; }
}
