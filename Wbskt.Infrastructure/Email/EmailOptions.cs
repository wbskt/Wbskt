namespace Wbskt.Infrastructure.Email;

/// <summary>
/// SMTP settings for whichever host is sending. Each host binds its own section — the Auth host from
/// <c>Auth:Email</c>, the workflow engine from <c>WorkflowEngine:Email</c> — so the two can be pointed
/// at different relays, and one host's mail failing does not implicate the other's.
/// </summary>
/// <remarks>
/// These settings live in host configuration rather than on a workflow node, deliberately: a workflow
/// definition is stored, versioned and readable by anyone with <c>workflows.read</c>, so an SMTP
/// password written into one would be readable by every member of the workspace and preserved in
/// every published version. The node carries only who to mail and what to say.
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
