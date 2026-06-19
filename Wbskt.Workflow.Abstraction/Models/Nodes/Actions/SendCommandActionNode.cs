using System.Text.Json.Serialization;

namespace Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
public sealed record SendCommandActionNode : BaseActionNode
{
    [JsonPropertyName("config")]
    public required SendCommandConfig Config { get; init; }


    [JsonIgnore]
    public override string Kind => NodeKind.ActionCommand;
}
