using System.Text.Json.Serialization;
using Wbskt.Workflow.Abstraction.Enums;

namespace Wbskt.Workflow.Abstraction.Models;

public sealed record RetryPolicy(
    [property: JsonPropertyName("strategy")][property: JsonConverter(typeof(JsonStringEnumConverter))] RetryStrategy Strategy,
    [property: JsonPropertyName("initialDelay")] TimeSpan InitialDelay,
    [property: JsonPropertyName("factor")] double? Factor,
    [property: JsonPropertyName("maxDelay")] TimeSpan? MaxDelay,
    [property: JsonPropertyName("maxAttempts")] int MaxAttempts,
    [property: JsonPropertyName("jitterPct")] int JitterPct,
    [property: JsonPropertyName("retryOn")] IReadOnlyCollection<string> RetryOn
);
