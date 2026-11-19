using Wbskt.Auth.Host.Models;
using Wbskt.Auth.Host.Services.Email;
using Wbskt.Auth.Host.Telemetry;

namespace Wbskt.Auth.Host.Services;

/// <summary>
/// Tells the owner of an account, by mail, about a security change they may not have made: the
/// password changed or reset, or the account locked by wrong passwords. Without it, someone whose
/// password was phished and changed only found out at their next failed sign-in, with nothing in
/// their inbox saying why (#946).
/// </summary>
/// <remarks>
/// Called once the change has committed. Never throws: the mailer only queues, and the change the
/// notice is about has already happened.
/// </remarks>
internal sealed class SecurityNotices(IAuthMailer mailer, MailCooldown mailCooldown, AuthMetrics metrics, TimeProvider time)
{
    /// <summary>
    /// The password was changed while signed in (<paramref name="how"/> is <c>changed</c>) or through a
    /// reset link (<c>reset</c>).
    /// </summary>
    public async Task PasswordChangedAsync(User user, string how, string ipAddress, CancellationToken cancellationToken) =>
        await mailer.QueuePasswordChangedAsync(user.Email, user.Username, how, time.GetUtcNow().UtcDateTime, ipAddress, cancellationToken);

    /// <summary>
    /// The account was just locked by wrong passwords. Under the mail cooldown, since whoever is
    /// guessing decides how often that happens.
    /// </summary>
    public async Task AccountLockedAsync(User user, DateTime lockedUntilUtc, string ipAddress, CancellationToken cancellationToken)
    {
        if (!await mailCooldown.TryAcquireAsync(user.Email, MailKind.AccountLocked))
        {
            metrics.RecordMailSuppressed(nameof(MailKind.AccountLocked));
            return;
        }

        await mailer.QueueAccountLockedAsync(user.Email, user.Username, lockedUntilUtc, ipAddress, cancellationToken);
    }
}
