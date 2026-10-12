using System.Text.Json.Serialization;
namespace Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
public sealed record TelegramConfig
{
    /// <summary>The Telegram bot to send as: its RefId, from the workspace's integrations.</summary>
    [JsonPropertyName("integrationRef")]
    public Guid? IntegrationRef { get; init; }

    [JsonPropertyName("chatId")]
    public required string ChatId { get; init; }

    [JsonPropertyName("message")]
    public required string Message { get; init; }

}

