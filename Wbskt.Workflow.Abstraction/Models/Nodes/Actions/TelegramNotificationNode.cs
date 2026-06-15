using System.Text.Json.Serialization;

namespace Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
public sealed record TelegramNotificationNode(
    Guid NodeId, string Name, IReadOnlyCollection<PortDefinition> Ports,
    [property: JsonPropertyName("config")] TelegramConfig Config,
    RetryPolicy? Retry = null, OnFailureConfig? OnFailure = null, CompensationDeclaration? Compensation = null
) : BaseActionNode(NodeId, Name, Ports, Retry, OnFailure, Compensation)
{
    [JsonIgnore]
    public override string Kind => NodeKind.ActionTelegram;
}
