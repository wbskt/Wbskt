using System.Text.Json.Serialization;

namespace Wbskt.Workflow.Abstraction.Models.Nodes;

[JsonConverter(typeof(BaseNodeJsonConverter))]
public abstract record BaseNode(
    [property: JsonPropertyName("nodeId")] Guid NodeId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("ports")] IReadOnlyCollection<PortDefinition> Ports
)
{
    [JsonIgnore]
    public abstract string Kind { get; }
}
