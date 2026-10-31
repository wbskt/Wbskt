using System.Text.Json.Serialization;
using Wbskt.Workflow.Abstraction.Templates;

namespace Wbskt.Workflow.Abstraction.Models.Nodes.Actions;

public sealed record TelegramConfig
{
    /// <summary>The Telegram bot to send as: its RefId, from the workspace's integrations.</summary>
    [JsonPropertyName("integrationRef")]
    public Guid? IntegrationRef { get; init; }

    /// <summary>A numeric chat id, or <c>@channelusername</c> for a public channel.</summary>
    [Templated(TemplateText.Line)]
    [JsonPropertyName("chatId")]
    public string ChatId { get; init; } = string.Empty;

    [Templated]
    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;

    /// <summary>Optional: <c>MarkdownV2</c> or <c>HTML</c>. Left out, the message is sent as plain text.</summary>
    [JsonPropertyName("parseMode")]
    public string? ParseMode { get; init; }

    /// <summary>When true, the message arrives without a sound.</summary>
    [JsonPropertyName("silent")]
    public bool Silent { get; init; }

    /// <summary>Optional: the forum topic to post into, in a group with topics.</summary>
    [JsonPropertyName("messageThreadId")]
    public string? MessageThreadId { get; init; }

    /// <summary>When true, no preview is shown for a link in the message.</summary>
    [JsonPropertyName("disableLinkPreview")]
    public bool DisableLinkPreview { get; init; }
}
