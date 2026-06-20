using System.Text.Json.Serialization;
using Wbskt.Workflow.Abstraction.Enums;

namespace Wbskt.Workflow.Abstraction.Models.SharedVariables;

public sealed record SharedVariableDeclaration
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("type")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required SharedVariableType Type { get; init; }

    [JsonPropertyName("default")]
    public System.Text.Json.JsonElement? Default { get; init; }

    [JsonPropertyName("resetPolicy")]
    public ResetPolicy? ResetPolicy { get; init; }

}

