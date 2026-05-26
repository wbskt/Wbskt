using System.Text.Json.Serialization;
using Wbskt.Workflow.Abstraction.Enums;
namespace Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
public sealed record JoinConfig(
    [property: JsonPropertyName("mode")][property: JsonConverter(typeof(JsonStringEnumConverter))] JoinMode Mode,
    [property: JsonPropertyName("quorumCount")] int? QuorumCount = null);
