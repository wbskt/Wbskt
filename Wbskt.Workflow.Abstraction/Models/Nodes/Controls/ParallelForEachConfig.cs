using System.Text.Json.Serialization;
namespace Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
public sealed record ParallelForEachConfig
{
    [JsonPropertyName("collection")]
    public string Collection { get; init; } = string.Empty;

}

