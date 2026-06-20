using System.Text.Json.Serialization;
using Wbskt.Workflow.Abstraction.Enums;

namespace Wbskt.Workflow.Abstraction.Models;

public sealed record RetryPolicy
{
    [JsonPropertyName("strategy")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required RetryStrategy Strategy { get; init; }

    [JsonPropertyName("initialDelay")]
    public required TimeSpan InitialDelay { get; init; }

    [JsonPropertyName("factor")]
    public double? Factor { get; init; }

    [JsonPropertyName("maxDelay")]
    public TimeSpan? MaxDelay { get; init; }

    [JsonPropertyName("maxAttempts")]
    public required int MaxAttempts { get; init; }

    [JsonPropertyName("jitterPct")]
    public required int JitterPct { get; init; }

    [JsonPropertyName("retryOn")]
    public required IReadOnlyCollection<string> RetryOn { get; init; }

}

