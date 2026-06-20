using System.Text.Json.Serialization;
namespace Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
public sealed record LogicGateConfig
{
    [JsonPropertyName("condition")]
    public required string Condition { get; init; }

}

