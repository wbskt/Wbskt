using System.Text.Json.Serialization;

namespace Wbskt.Workflow.Abstraction.Models.Bookmarks;

public sealed record TimerWakeCondition(
    [property: JsonPropertyName("at")] DateTime At
) : WakeCondition
{
    [JsonIgnore]
    public override string Kind => "timer";

    /// <summary>A timer is woken by its time alone, whatever TTL it carries.</summary>
    public override DateTime? ExpiresAt(DateTime nowUtc) => At;
}
