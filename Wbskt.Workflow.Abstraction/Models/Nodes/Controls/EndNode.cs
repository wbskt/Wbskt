using System.Text.Json.Serialization;

namespace Wbskt.Workflow.Abstraction.Models.Nodes.Controls;

/// <summary>
/// Explicit terminal node — completes the branch when reached (no outbound edges required).
/// </summary>
public sealed record EndNode : BaseNode
{
    [JsonPropertyName("config")]
    public EndConfig? Config { get; init; }

    [JsonIgnore]
    public override string Kind => NodeKind.ControlEnd;
}
