using System.Text.Json.Serialization;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models.Expressions;

namespace Wbskt.Workflow.Abstraction.Models.Triggers;

public sealed record ClientTriggerConfig
{
    public const int MaxHoldSeconds = 24 * 60 * 60;

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

    /// <summary>
    /// When above zero, <see cref="Filter"/> must keep matching for this many seconds before a run
    /// starts, and the trigger then stays quiet until a message stops matching. A fridge door left
    /// open or a reading above a limit for ten minutes is the motivating case: one noisy reading
    /// should not page anyone, and a reading that stays bad should page them once, not every message.
    /// Requires a filter.
    /// </summary>
    [JsonPropertyName("holdSeconds")]
    public int HoldSeconds { get; init; } = 0;
}
