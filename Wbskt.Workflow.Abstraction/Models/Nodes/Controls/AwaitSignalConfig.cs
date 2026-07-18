using System.Text.Json.Serialization;
namespace Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
public sealed record AwaitSignalConfig
{
    [JsonPropertyName("signalName")]
    public required string SignalName { get; init; }

    [JsonPropertyName("correlation")]
    public string? Correlation { get; init; } = null;

    [JsonPropertyName("ttl")]
    public TimeSpan? Ttl { get; init; } = null;

}

