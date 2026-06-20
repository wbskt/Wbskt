using System.Text.Json.Serialization;
using Wbskt.Workflow.Abstraction.Enums;
namespace Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
public sealed record JoinConfig
{
    [JsonPropertyName("mode")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required JoinMode Mode { get; init; }

    [JsonPropertyName("quorumCount")]
    public int? QuorumCount { get; init; } = null;

}

