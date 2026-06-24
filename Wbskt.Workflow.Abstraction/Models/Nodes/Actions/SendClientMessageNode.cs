using System.Text.Json.Serialization;

namespace Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
public sealed record SendClientMessageNode : BaseActionNode
{
    [JsonPropertyName("config")]
    public required SendClientMessageConfig Config { get; init; }


    [JsonIgnore]
    public override string Kind => NodeKind.ActionClientMessage;
}
