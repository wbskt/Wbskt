using System.Text.Json.Serialization;

namespace Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
public sealed record ToastNotificationNode : BaseActionNode
{
    [JsonPropertyName("config")]
    public required ToastConfig Config { get; init; }


    [JsonIgnore]
    public override string Kind => NodeKind.ActionToast;
}
