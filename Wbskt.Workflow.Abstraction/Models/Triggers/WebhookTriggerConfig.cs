using System.Text.Json.Serialization;
using Wbskt.Workflow.Abstraction.Enums;

namespace Wbskt.Workflow.Abstraction.Models.Triggers;

public sealed record WebhookTriggerConfig(
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("method")] string Method,
    [property: JsonPropertyName("correlationKey")] string? CorrelationKey = null,
    [property: JsonPropertyName("concurrencyPolicy")][property: JsonConverter(typeof(JsonStringEnumConverter))] WorkflowConcurrencyPolicy ConcurrencyPolicy = WorkflowConcurrencyPolicy.Queue
);
