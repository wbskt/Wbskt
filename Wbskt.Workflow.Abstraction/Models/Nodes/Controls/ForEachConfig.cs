using System.Text.Json.Serialization;
namespace Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
public sealed record ForEachConfig
{
    [JsonPropertyName("collection")]
    public string Collection { get; init; } = string.Empty;

}

