using Microsoft.Extensions.Options;
using Wbskt.Infrastructure.Email;

namespace Wbskt.Auth.Host.Services.Email;

/// <summary>
/// Renders each message and hands it to <see cref="OutboundMailQueue"/>. Rendering happens here,
/// on the caller's thread, so the raw token is turned into a body before it is ever queued.
/// </summary>
internal sealed class QueuedAuthMailer(
    OutboundMailQueue queue,
    IEmailSender sender,
    IOptions<AuthEmailOptions> options,
    ILogger<QueuedAuthMailer> logger) : IAuthMailer
{
    private readonly AuthEmailOptions _options = options.Value;

    public bool IsConfigured => sender.IsConfigured;

    public ValueTask QueueInvitationAsync(string to, string tenantName, string token, DateTime expiresAtUtc, CancellationToken ct) =>
        Enqueue(AuthEmailTemplates.Invitation(_options, to, tenantName, token, expiresAtUtc), nameof(QueueInvitationAsync));

    public ValueTask QueueEmailVerificationAsync(string to, string username, string token, DateTime expiresAtUtc, CancellationToken ct) =>
        Enqueue(AuthEmailTemplates.EmailVerification(_options, to, username, token, expiresAtUtc), nameof(QueueEmailVerificationAsync));

    public ValueTask QueuePasswordResetAsync(string to, string username, string token, DateTime expiresAtUtc, CancellationToken ct) =>
        Enqueue(AuthEmailTemplates.PasswordReset(_options, to, username, token, expiresAtUtc), nameof(QueuePasswordResetAsync));

    public ValueTask QueueAccountAlreadyExistsAsync(string to, string username, CancellationToken ct) =>
        Enqueue(AuthEmailTemplates.AccountAlreadyExists(_options, to, username), nameof(QueueAccountAlreadyExistsAsync));

    private ValueTask Enqueue(OutboundMail mail, string kind)
    {
        // Neither branch below throws. Every caller is midway through an operation that has already
        // succeeded - an account created, a password reset issued - and failing it because the relay
        // is unreachable would undo work the user can see for a reason they cannot act on.
        if (!sender.IsConfigured)
        {
            logger.LogError(
                "No SMTP relay is configured (Auth:Email), so {MailKind} was not sent. Sign-in requires a verified address, " +
                "so accounts created while this is true cannot be used.", kind);
            return ValueTask.CompletedTask;
        }

        if (!queue.TryWrite(mail))
        {
            logger.LogError("The outbound mail queue is full; {MailKind} was dropped. The relay is not keeping up.", kind);
        }

        return ValueTask.CompletedTask;
    }
}
