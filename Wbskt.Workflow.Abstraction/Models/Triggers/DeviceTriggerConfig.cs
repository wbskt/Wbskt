using System.Text.Json.Serialization;
using Wbskt.Workflow.Abstraction.Enums;

namespace Wbskt.Workflow.Abstraction.Models.Triggers;

public sealed record DeviceTriggerConfig
{
    [JsonPropertyName("deviceRef")]
    public required string DeviceRef { get; init; }

    [JsonPropertyName("event")]
    public required string Event { get; init; }

    [JsonPropertyName("correlationKey")]
    public string? CorrelationKey { get; init; } = null;

    [JsonPropertyName("concurrencyPolicy")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public WorkflowConcurrencyPolicy ConcurrencyPolicy { get; init; } = WorkflowConcurrencyPolicy.Queue;

}

