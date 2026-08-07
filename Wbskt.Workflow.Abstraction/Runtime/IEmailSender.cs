namespace Wbskt.Workflow.Abstraction.Runtime;

/// <summary>
/// Sends one email. An abstraction rather than an SMTP call inline in the executor, because SMTP has no
/// injectable seam of its own - this is what makes the email node testable at all, and what lets an
/// operator swap in a provider API later without touching the node.
/// </summary>
public interface IEmailSender
{
    /// <summary>Whether the host is configured to send. False makes the node fail with an explanation
    /// rather than throw somewhere inside the SMTP stack.</summary>
    bool IsConfigured { get; }

    Task SendAsync(string to, string subject, string body, CancellationToken ct);
}
