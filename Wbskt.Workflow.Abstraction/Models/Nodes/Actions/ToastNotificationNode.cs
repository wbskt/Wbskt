using System.Text.Json.Serialization;
using Wbskt.Workflow.Abstraction.Templates;

namespace Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
public sealed record ToastNotificationNode : BaseActionNode, ITemplatedStep
{
    [JsonPropertyName("config")]
    public required ToastConfig Config { get; init; }

    [JsonIgnore]
    object? ITemplatedStep.TemplatedConfig => Config;


    [JsonIgnore]
    public override string Kind => NodeKind.ActionToast;
}
