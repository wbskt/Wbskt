using System.Text.Json.Serialization;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models.Expressions;

namespace Wbskt.Workflow.Abstraction.Models.Triggers;

public sealed record WebhookTriggerConfig
{
    [JsonPropertyName("path")]
    public required string Path { get; init; }

    [JsonPropertyName("method")]
    public required string Method { get; init; }

    [JsonPropertyName("correlationKey")]
    public string? CorrelationKey { get; init; } = null;

    [JsonPropertyName("concurrencyPolicy")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public WorkflowConcurrencyPolicy ConcurrencyPolicy { get; init; } = WorkflowConcurrencyPolicy.Queue;

    /// <summary>
    /// Evaluated against the inbound payload before a run is started. A run only starts when this
    /// evaluates to <c>true</c>; null means every call to the path starts one. Use it to avoid paying
    /// for - and having to explain - runs that begin only to immediately decide they are irrelevant.
    /// </summary>
    [JsonPropertyName("filter")]
    public WorkflowExpression? Filter { get; init; } = null;

    /// <summary>
    /// A shared secret the caller must present in the <c>X-Wbskt-Secret</c> header. Without one, the only
    /// thing protecting the trigger is the secrecy of its URL. A mismatch is indistinguishable from a
    /// match in the response, so this cannot be probed.
    /// </summary>
    [JsonPropertyName("secret")]
    public string? Secret { get; init; } = null;
}

