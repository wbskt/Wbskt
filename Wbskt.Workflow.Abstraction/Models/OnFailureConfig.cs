using System.Text.Json.Serialization;
using Wbskt.Workflow.Abstraction.Enums;

namespace Wbskt.Workflow.Abstraction.Models;

public sealed record OnFailureConfig(
    [property: JsonPropertyName("outcome")][property: JsonConverter(typeof(JsonStringEnumConverter))] ErrorOutcome Outcome,
    [property: JsonPropertyName("compensate")] CompensationDeclaration? Compensate
);
