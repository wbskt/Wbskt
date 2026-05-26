using System.Text.Json.Serialization;
namespace Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
public sealed record TelegramConfig(
    [property: JsonPropertyName("chatId")] string ChatId,
    [property: JsonPropertyName("message")] string Message);
