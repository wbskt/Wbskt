namespace Wbskt.Infrastructure.Email;

/// <summary>
/// SMTP settings for a host's own mail, bound from its own section (the Auth host's is
/// <c>Auth:Email</c>). Workflow email does not use these: it goes through each workspace's SMTP
/// integration, so a workflow author can never send as the platform's own relay.
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
