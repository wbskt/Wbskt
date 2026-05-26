using System.Text.Json.Serialization;
using Wbskt.Workflow.Abstraction.Enums;

namespace Wbskt.Workflow.Abstraction.Models.Triggers;

public sealed record DeviceTriggerConfig(
    [property: JsonPropertyName("deviceRef")] string DeviceRef,
    [property: JsonPropertyName("event")] string Event,
    [property: JsonPropertyName("correlationKey")] string? CorrelationKey = null,
    [property: JsonPropertyName("concurrencyPolicy")][property: JsonConverter(typeof(JsonStringEnumConverter))] WorkflowConcurrencyPolicy ConcurrencyPolicy = WorkflowConcurrencyPolicy.Queue
);
