using System.Text.Json.Serialization;

namespace Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
public sealed record EmailNotificationNode(
    Guid NodeId, string Name, IReadOnlyCollection<PortDefinition> Ports,
    [property: JsonPropertyName("config")] EmailConfig Config,
    RetryPolicy? Retry = null, OnFailureConfig? OnFailure = null, CompensationDeclaration? Compensation = null
) : BaseActionNode(NodeId, Name, Ports, Retry, OnFailure, Compensation)
{
    [JsonIgnore]
    public override string Kind => NodeKind.ActionEmail;
}
