using System.Text.Json.Serialization;
using Wbskt.Workflow.Abstraction.Enums;

namespace Wbskt.Workflow.Abstraction.Models.Triggers;

public sealed record ClientPresenceTriggerConfig
{
    public const int DefaultForSeconds = 60;
    public const int MaxForSeconds = 24 * 60 * 60;

    // Empty rather than required: the validator lists it as TRIGGER_CLIENT_REQUIRED.
    [JsonPropertyName("clientRef")]
    public string ClientRef { get; init; } = string.Empty;

    [JsonPropertyName("state")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ClientPresenceState State { get; init; } = ClientPresenceState.Offline;

    /// <summary>
    /// How long the client must stay in <see cref="State"/> before a run starts. A client that
    /// reboots or roams between access points drops its socket for a few seconds; without a grace
    /// period every blip would page its owner.
    /// </summary>
    [JsonPropertyName("forSeconds")]
    public int ForSeconds { get; init; } = DefaultForSeconds;

    [JsonPropertyName("correlationKey")]
    public string? CorrelationKey { get; init; } = null;

    [JsonPropertyName("concurrencyPolicy")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public WorkflowConcurrencyPolicy ConcurrencyPolicy { get; init; } = WorkflowConcurrencyPolicy.Queue;
}
