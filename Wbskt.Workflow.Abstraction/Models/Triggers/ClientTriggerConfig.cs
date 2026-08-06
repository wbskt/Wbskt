using System.Text.Json.Serialization;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models.Expressions;

namespace Wbskt.Workflow.Abstraction.Models.Triggers;

public sealed record ClientTriggerConfig
{
    [JsonPropertyName("clientRef")]
    public required string ClientRef { get; init; }

    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("correlationKey")]
    public string? CorrelationKey { get; init; } = null;

    [JsonPropertyName("concurrencyPolicy")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public WorkflowConcurrencyPolicy ConcurrencyPolicy { get; init; } = WorkflowConcurrencyPolicy.Queue;

    /// <summary>
    /// Evaluated against the inbound payload before a run is started. A run only starts when this
    /// evaluates to <c>true</c>; null means every matching message starts one. A chatty device that
    /// only matters above a threshold is the motivating case.
    /// </summary>
    [JsonPropertyName("filter")]
    public WorkflowExpression? Filter { get; init; } = null;
}

