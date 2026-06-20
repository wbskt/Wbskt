using System.Text.Json.Serialization;
namespace Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
public sealed record ForEachConfig
{
    [JsonPropertyName("collection")]
    public required string Collection { get; init; }

}

