using System.Text.Json.Serialization;
namespace Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
public sealed record WaitForHttpConfig(
    [property: JsonPropertyName("ttl")] TimeSpan Ttl,
    [property: JsonPropertyName("onTimeout")] string? OnTimeout = null);
