using System.Text.Json.Serialization;
using Wbskt.Workflow.Abstraction.Integrations;

namespace Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
public sealed record TelegramNotificationNode : BaseActionNode, IIntegrationStep
{
    [JsonPropertyName("config")]
    public required TelegramConfig Config { get; init; }


    [JsonIgnore]
    public Guid? IntegrationRef => Config?.IntegrationRef;

    [JsonIgnore]
    public override string Kind => NodeKind.ActionTelegram;
}
