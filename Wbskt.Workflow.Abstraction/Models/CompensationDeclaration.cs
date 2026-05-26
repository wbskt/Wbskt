using System.Text.Json.Serialization;

namespace Wbskt.Workflow.Abstraction.Models;

public sealed record CompensationDeclaration(
    [property: JsonPropertyName("nodeId")] string NodeId,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("config")] System.Text.Json.JsonElement? Config
);
