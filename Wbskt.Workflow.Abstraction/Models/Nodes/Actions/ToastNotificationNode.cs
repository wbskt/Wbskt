using System.Text.Json.Serialization;

namespace Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
public sealed record ToastNotificationNode(
    Guid NodeId, string Name, IReadOnlyCollection<PortDefinition> Ports,
    [property: JsonPropertyName("config")] ToastConfig Config,
    RetryPolicy? Retry = null, OnFailureConfig? OnFailure = null, CompensationDeclaration? Compensation = null
) : BaseActionNode(NodeId, Name, Ports, Retry, OnFailure, Compensation)
{
    [JsonIgnore]
    public override string Kind => NodeKind.ActionToast;
}
