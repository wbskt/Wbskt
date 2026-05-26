using System.Text.Json.Serialization;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes;
namespace Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
public sealed record WebhookNotificationNode(
    Guid NodeId, string Name, IReadOnlyCollection<PortDefinition> Ports,
    [property: JsonPropertyName("config")] WebhookNotificationConfig Config,
    RetryPolicy? Retry = null, OnFailureConfig? OnFailure = null
) : BaseActionNode(NodeId, Name, Ports, Retry, OnFailure)
{
    [JsonIgnore]
    public override string Kind => NodeKind.ActionWebhook;
}
