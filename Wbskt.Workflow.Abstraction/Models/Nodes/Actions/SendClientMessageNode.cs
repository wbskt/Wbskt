using System.Text.Json.Serialization;
using Wbskt.Workflow.Abstraction.Templates;

namespace Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
public sealed record SendClientMessageNode : BaseActionNode, ITemplatedStep
{
    [JsonPropertyName("config")]
    public required SendClientMessageConfig Config { get; init; }

    [JsonIgnore]
    object? ITemplatedStep.TemplatedConfig => Config;


    [JsonIgnore]
    public override string Kind => NodeKind.ActionClientMessage;
}
