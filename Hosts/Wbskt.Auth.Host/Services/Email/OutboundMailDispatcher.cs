using Wbskt.Infrastructure.Email;

namespace Wbskt.Auth.Host.Services.Email;

/// <summary>
/// Drains <see cref="OutboundMailQueue"/> and hands each message to the relay, off the request path.
/// </summary>
internal sealed class OutboundMailDispatcher(
    OutboundMailQueue queue,
    IEmailSender sender,
    ILogger<OutboundMailDispatcher> logger) : BackgroundService
{
    /// <summary>
    /// Relays fail transiently — greylisting, a connection reset, a restart — far more often than
    /// permanently, and the alternative to a retry here is losing the message. Bounded at three so a
    /// permanently broken relay cannot wedge the drain loop.
    /// </summary>
    private static readonly TimeSpan[] RetryDelays = [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(8)];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!sender.IsConfigured)
        {
            // Loud, and at startup rather than at the first registration: with sign-in gated on a
            // verified address, an unconfigured relay means nobody new can get in, and that should
            // not first become visible as a user complaint.
            logger.LogError(
                "No SMTP relay is configured (Auth:Email). Invitations, verification and password-reset mail cannot be sent, " +
                "and because sign-in requires a verified address, no new account will be usable.");
        }

        await foreach (OutboundMail mail in queue.ReadAllAsync(stoppingToken))
        {
            await SendWithRetryAsync(mail, stoppingToken);
        }
    }

    private async Task SendWithRetryAsync(OutboundMail mail, CancellationToken ct)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                await sender.SendAsync(mail.To, mail.Subject, mail.HtmlBody, EmailBodyFormat.Html, ct);
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Shutdown. The message is lost; every one of them has a user-triggered way to be
                // reissued, which is why that is acceptable and why nothing here blocks shutdown.
                return;
            }
            catch (FormatException ex)
            {
                // A malformed address fails identically on every attempt. The address is logged
                // because it is the only way to find the account that has one; the body is not.
                logger.LogWarning(ex, "Mail to {Recipient} was not sent: the address is malformed.", mail.To);
                return;
            }
            catch (Exception ex)
            {
                if (attempt >= RetryDelays.Length)
                {
                    logger.LogError(ex, "Mail to {Recipient} was not sent after {Attempts} attempts.", mail.To, attempt + 1);
                    return;
                }

                logger.LogWarning(ex, "Mail to {Recipient} failed on attempt {Attempt}; retrying.", mail.To, attempt + 1);

                try
                {
                    await Task.Delay(RetryDelays[attempt], ct);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }
}
