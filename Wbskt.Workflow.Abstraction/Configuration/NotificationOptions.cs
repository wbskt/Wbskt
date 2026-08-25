namespace Wbskt.Workflow.Abstraction.Configuration;

/// <summary>
/// Telegram Bot API settings for <c>action:telegram</c>, bound from <c>WorkflowEngine:Telegram</c>.
/// </summary>
/// <remarks>
/// The bot token is a credential and is kept out of the definition for the same reason the SMTP
/// settings are (see Wbskt.Infrastructure.Email.EmailOptions) - a token in a definition is readable
/// by the whole workspace and frozen into every version. The node carries only the chat id and the
/// message.
/// </remarks>
public sealed class TelegramOptions
{
    public string? BotToken { get; init; }

    /// <summary>Overridable for testing and for self-hosted Bot API servers.</summary>
    public string ApiBaseUrl { get; init; } = "https://api.telegram.org";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(BotToken);
}
