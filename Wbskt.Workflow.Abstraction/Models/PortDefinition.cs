using System.Text.Json.Serialization;
using Wbskt.Workflow.Abstraction.Enums;

namespace Wbskt.Workflow.Abstraction.Models;

public sealed record PortDefinition
{
    [JsonPropertyName("portId")]
    public required string PortId { get; init; }

    [JsonPropertyName("direction")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required PortDirection Direction { get; init; }

    [JsonPropertyName("label")]
    public required string Label { get; init; }

}

