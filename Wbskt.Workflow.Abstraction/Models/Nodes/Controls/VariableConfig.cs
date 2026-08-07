using System.Text.Json.Serialization;
using Wbskt.Workflow.Abstraction.Enums;
namespace Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
public sealed record VariableConfig
{
    [JsonPropertyName("scope")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required VariableScope Scope { get; init; }

    [JsonPropertyName("op")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required VariableOperation Op { get; init; }

    [JsonPropertyName("var")]
    public required string Var { get; init; }

    [JsonPropertyName("value")]
    public System.Text.Json.JsonElement? Value { get; init; } = null;

    /// <summary>
    /// The value <see cref="VariableOperation.CompareAndSet"/> requires the variable to currently hold
    /// for the write to happen. Like <see cref="Value"/>, it may be a plain literal or an expression
    /// tree. Ignored by every other operation.
    /// </summary>
    [JsonPropertyName("expected")]
    public System.Text.Json.JsonElement? Expected { get; init; } = null;
}

