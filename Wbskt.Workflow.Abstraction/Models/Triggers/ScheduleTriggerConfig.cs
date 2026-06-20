using System.Text.Json.Serialization;

namespace Wbskt.Workflow.Abstraction.Models.Triggers;

public sealed record ScheduleTriggerConfig
{
    [JsonPropertyName("cron")]
    public required string Cron { get; init; }

    [JsonPropertyName("correlationKey")]
    public string? CorrelationKey { get; init; } = null;

}

