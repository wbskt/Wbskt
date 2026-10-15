using System.Text.Json.Serialization;
using Wbskt.Workflow.Abstraction.Enums;

namespace Wbskt.Workflow.Abstraction.Models;

public sealed record OnFailureConfig
{
    [JsonPropertyName("outcome")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required ErrorOutcome Outcome { get; init; }

    [JsonPropertyName("targetNodeId")]
    public Guid? TargetNodeId { get; init; } = null;

}

