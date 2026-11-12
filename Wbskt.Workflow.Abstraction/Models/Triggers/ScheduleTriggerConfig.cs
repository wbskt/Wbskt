using System.Text.Json.Serialization;

namespace Wbskt.Workflow.Abstraction.Models.Triggers;

public sealed record ScheduleTriggerConfig
{
    // Empty rather than required: the validator lists it as INVALID_CRON.
    [JsonPropertyName("cron")]
    public string Cron { get; init; } = string.Empty;

    [JsonPropertyName("correlationKey")]
    public string? CorrelationKey { get; init; } = null;

}

