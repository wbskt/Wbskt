namespace Wbskt.Workflow.Abstraction.Configuration;

/// <summary>
/// SMTP settings for <c>action:email</c>, bound from <c>WorkflowEngine:Email</c>.
/// </summary>
/// <remarks>
/// Credentials live here rather than on the node, deliberately: a workflow definition is stored,
/// versioned and readable by anyone with <c>workflows.read</c>, so an SMTP password written into one
/// would be readable by every member of the workspace and preserved in every published version.
/// The node carries only who to mail and what to say.
/// </remarks>
public sealed class EmailOptions
{
    public string? Host { get; init; }
    public int Port { get; init; } = 587;

    /// <summary>Upgrade the connection with STARTTLS. Off only makes sense against a local relay.</summary>
    public bool UseStartTls { get; init; } = true;

    public string? UserName { get; init; }
    public string? Password { get; init; }

    /// <summary>The envelope sender. Required - most relays reject mail without one.</summary>
    public string? FromAddress { get; init; }

    public string? FromDisplayName { get; init; }

    /// <summary>Whether enough is configured to send at all.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(FromAddress);
}

/// <summary>
/// Telegram Bot API settings for <c>action:telegram</c>, bound from <c>WorkflowEngine:Telegram</c>.
/// </summary>
/// <remarks>
/// The bot token is a credential and is kept out of the definition for the same reason as the SMTP
/// password above - a token in a definition is readable by the whole workspace and frozen into every
/// version. The node carries only the chat id and the message.
/// </remarks>
public sealed class TelegramOptions
{
    public string? BotToken { get; init; }

    /// <summary>Overridable for testing and for self-hosted Bot API servers.</summary>
    public string ApiBaseUrl { get; init; } = "https://api.telegram.org";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(BotToken);
}
