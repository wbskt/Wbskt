using System.Text.Json.Serialization;

namespace Wbskt.Workflow.Abstraction.Models;

public sealed record CompensationDeclaration
{
    [JsonPropertyName("nodeId")]
    public required Guid NodeId { get; init; }

    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    [JsonPropertyName("config")]
    public System.Text.Json.JsonElement? Config { get; init; }

}

