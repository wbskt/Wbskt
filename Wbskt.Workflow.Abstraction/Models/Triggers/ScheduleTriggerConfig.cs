using System.Text.Json.Serialization;

namespace Wbskt.Workflow.Abstraction.Models.Triggers;

public sealed record ScheduleTriggerConfig(
    [property: JsonPropertyName("cron")] string Cron,
    [property: JsonPropertyName("correlationKey")] string? CorrelationKey = null
);
