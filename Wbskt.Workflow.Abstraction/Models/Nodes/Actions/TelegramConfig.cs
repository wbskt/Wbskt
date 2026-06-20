using System.Text.Json.Serialization;
namespace Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
public sealed record TelegramConfig
{
    [JsonPropertyName("chatId")]
    public required string ChatId { get; init; }

    [JsonPropertyName("message")]
    public required string Message { get; init; }

}

