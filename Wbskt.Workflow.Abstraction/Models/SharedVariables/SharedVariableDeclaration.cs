using System.Text.Json.Serialization;
using Wbskt.Workflow.Abstraction.Enums;

namespace Wbskt.Workflow.Abstraction.Models.SharedVariables;

public sealed record SharedVariableDeclaration(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("type")][property: JsonConverter(typeof(JsonStringEnumConverter))] SharedVariableType Type,
    [property: JsonPropertyName("default")] System.Text.Json.JsonElement? Default,
    [property: JsonPropertyName("resetPolicy")] ResetPolicy? ResetPolicy
);
