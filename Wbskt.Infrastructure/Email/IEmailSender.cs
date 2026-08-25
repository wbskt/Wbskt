namespace Wbskt.Infrastructure.Email;

/// <summary>
/// How the recipient's client should render the body. An explicit argument on every send rather than
/// a property of the sender, because the safe answer differs by caller and the difference is a
/// security boundary: a host template is ours and may be HTML, a workflow node's body is written by
/// a workspace member and must not be.
/// </summary>
public enum EmailBodyFormat
{
    /// <summary>Rendered literally. The only safe choice for a body this platform did not author.</summary>
    PlainText,

    /// <summary>Rendered as markup. Only for bodies produced from a template in this repository.</summary>
    Html
}

/// <summary>
/// Sends one email. An abstraction rather than an SMTP call inline in the caller, because SMTP has no
/// injectable seam of its own - this is what makes a sending path testable at all, and what lets an
/// operator swap in a provider API later without touching the callers.
/// </summary>
public interface IEmailSender
{
    /// <summary>Whether the host is configured to send. False makes the caller fail with an explanation
    /// rather than throw somewhere inside the SMTP stack.</summary>
    bool IsConfigured { get; }

    Task SendAsync(string to, string subject, string body, EmailBodyFormat format, CancellationToken ct);
}
