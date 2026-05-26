using System.Text.Json.Serialization;
using Wbskt.Workflow.Abstraction.Enums;

namespace Wbskt.Workflow.Abstraction.Models;

public sealed record PortDefinition(
    [property: JsonPropertyName("portId")] string PortId,
    [property: JsonPropertyName("direction")][property: JsonConverter(typeof(JsonStringEnumConverter))] PortDirection Direction,
    [property: JsonPropertyName("label")] string Label
);
