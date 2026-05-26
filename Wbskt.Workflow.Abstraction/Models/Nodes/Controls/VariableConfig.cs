using System.Text.Json.Serialization;
using Wbskt.Workflow.Abstraction.Enums;
namespace Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
public sealed record VariableConfig(
    [property: JsonPropertyName("scope")][property: JsonConverter(typeof(JsonStringEnumConverter))] VariableScope Scope,
    [property: JsonPropertyName("op")][property: JsonConverter(typeof(JsonStringEnumConverter))] VariableOperation Op,
    [property: JsonPropertyName("var")] string Var,
    [property: JsonPropertyName("value")] System.Text.Json.JsonElement? Value = null);
