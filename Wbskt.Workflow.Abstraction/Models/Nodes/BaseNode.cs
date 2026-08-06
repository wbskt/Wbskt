using System.Text.Json.Serialization;

namespace Wbskt.Workflow.Abstraction.Models.Nodes;

// TODO(arch): could move to System.Text.Json [JsonPolymorphic]; the hand-rolled converter works and
// writes "kind" first, so this is cosmetic.
[JsonConverter(typeof(BaseNodeJsonConverter))]
public abstract record BaseNode
{
    [JsonIgnore]
    public abstract string Kind { get; }

    [JsonPropertyName("nodeId")]
    public Guid NodeId { get; init; }
    
    [JsonPropertyName("name")]
    public required string Name { get; init; }
    
    [JsonPropertyName("ports")]
    public required IReadOnlyCollection<PortDefinition> Ports { get; init; }

    [JsonPropertyName("posX")]
    public double PosX { get; init; } = 0;
    
    [JsonPropertyName("posY")]
    public double PosY { get; init; } = 0;
}
