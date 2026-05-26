using System.Text.Json.Serialization;
namespace Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
public sealed record AwaitSignalConfig(
    [property: JsonPropertyName("signalName")] string SignalName,
    [property: JsonPropertyName("correlation")] string? Correlation = null,
    [property: JsonPropertyName("ttl")] TimeSpan? Ttl = null,
    [property: JsonPropertyName("onTimeout")] string? OnTimeout = null);
