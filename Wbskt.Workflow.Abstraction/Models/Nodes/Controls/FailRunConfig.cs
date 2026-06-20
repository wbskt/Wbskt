using System.Text.Json.Serialization;
namespace Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
public sealed record FailRunConfig
{
    [JsonPropertyName("reason")]
    public string? Reason { get; init; } = null;

}

