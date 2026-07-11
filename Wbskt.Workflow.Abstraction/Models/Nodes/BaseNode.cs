using System.Text.Json.Serialization;

namespace Wbskt.Workflow.Abstraction.Models.Nodes;

// [RJ]: TODO: move to polymorphic
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
