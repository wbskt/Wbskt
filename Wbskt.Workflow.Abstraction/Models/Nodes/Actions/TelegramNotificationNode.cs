using System.Text.Json.Serialization;

namespace Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
public sealed record TelegramNotificationNode : BaseActionNode
{
    [JsonPropertyName("config")]
    public required TelegramConfig Config { get; init; }


    [JsonIgnore]
    public override string Kind => NodeKind.ActionTelegram;
}
