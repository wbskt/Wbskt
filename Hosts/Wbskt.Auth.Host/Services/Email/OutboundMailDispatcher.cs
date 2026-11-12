using Wbskt.Auth.Host.Telemetry;
using Wbskt.Infrastructure.Email;

namespace Wbskt.Auth.Host.Services.Email;

/// <summary>
/// Drains <see cref="OutboundMailQueue"/> and hands each message to the relay, off the request path.
/// </summary>
internal sealed class OutboundMailDispatcher : BackgroundService
{
    /// <summary>
    /// Relays fail transiently — greylisting, a connection reset, a restart — far more often than
    /// permanently, and the alternative to a retry here is losing the message. Two delays means three
    /// attempts in total, bounded so a permanently broken relay cannot wedge the drain loop: this
    /// loop is sequential, so every second spent retrying one message is a second the rest wait.
    /// </summary>
    private static readonly TimeSpan[] DefaultRetryDelays = [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(8)];

    private readonly OutboundMailQueue _queue;
    private readonly IEmailSender _sender;
    private readonly ILogger<OutboundMailDispatcher> _logger;
    private readonly AuthMetrics _metrics;
    private readonly IReadOnlyList<TimeSpan> _retryDelays;

    /// <remarks>
    /// <c>retryDelays</c> is overridable so the suite can exercise the retry ladder without waiting
    /// ten seconds for it. DI never supplies it; production uses <see cref="DefaultRetryDelays"/>.
    /// </remarks>
    public OutboundMailDispatcher(
        OutboundMailQueue queue,
        IEmailSender sender,
        ILogger<OutboundMailDispatcher> logger,
        AuthMetrics metrics,
        IReadOnlyList<TimeSpan>? retryDelays = null)
    {
        _queue = queue;
        _sender = sender;
        _logger = logger;
        _metrics = metrics;
        _retryDelays = retryDelays ?? DefaultRetryDelays;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_sender.IsConfigured)
        {
            // Loud, and at startup rather than at the first registration: with sign-in gated on a
            // verified address, an unconfigured relay means nobody new can get in, and that should
            // not first become visible as a user complaint.
            _logger.LogError(
                "No SMTP relay is configured (Auth:Email). Invitations, verification and password-reset mail cannot be sent, " +
                "and because sign-in requires a verified address, no new account will be usable.");
        }

        await foreach (OutboundMail mail in _queue.ReadAllAsync(stoppingToken))
        {
            await SendWithRetryAsync(mail, stoppingToken);
        }
    }

    private async Task SendWithRetryAsync(OutboundMail mail, CancellationToken ct)
    {
        // Never the address, only its fingerprint (EmailFingerprint), and never the subject, which can
        // carry a tenant's name; never the body either, which carries the token.
        string recipient = EmailFingerprint.Of(mail.To);
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                await _sender.SendMultipartAsync(mail.To, mail.Subject, mail.TextBody, mail.HtmlBody, ct);

                // The relay took it; delivery from there is the relay's. Without this line a missing
                // mail cannot be told apart from one that was never handed over.
                _metrics.RecordMail(MailOutcome.Sent);
                _logger.LogInformation("Mail {MailKind} to {RecipientFingerprint} accepted by the relay.", mail.Kind, recipient);
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Shutdown. The message is lost; every one of them has a user-triggered way to be
                // reissued, which is why that is acceptable and why nothing here blocks shutdown.
                return;
            }
            catch (Exception ex) when (MailFailure.IsMalformedRecipient(ex, mail.To))
            {
                // Fails identically on every attempt. An operator finds the account by checking a
                // suspect address against the fingerprint.
                _metrics.RecordMail(MailOutcome.InvalidAddress);
                _logger.LogWarning("Mail {MailKind} to {RecipientFingerprint} was not sent: the address is malformed.", mail.Kind, recipient);
                return;
            }
            catch (Exception ex) when (MailFailure.IsPermanentRecipientRefusal(ex))
            {
                // The relay will not take this recipient, so trying again only delays the rest. One
                // bad mailbox is not the relay failing, so it is counted apart and does not alert.
                _metrics.RecordMail(MailOutcome.Rejected);
                _logger.LogWarning("Mail {MailKind} to {RecipientFingerprint} was refused by the relay: {Failure}.", mail.Kind, recipient, MailFailure.Describe(ex));
                return;
            }
            catch (Exception ex)
            {
                if (attempt >= _retryDelays.Count)
                {
                    _metrics.RecordMail(MailOutcome.Dropped);
                    _logger.LogError("Mail {MailKind} to {RecipientFingerprint} was not sent after {Attempts} attempts: {Failure}.", mail.Kind, recipient, attempt + 1, MailFailure.Describe(ex));
                    return;
                }

                _metrics.RecordMail(MailOutcome.Retried);
                _logger.LogWarning("Mail {MailKind} to {RecipientFingerprint} failed on attempt {Attempt}; retrying: {Failure}.", mail.Kind, recipient, attempt + 1, MailFailure.Describe(ex));

                try
                {
                    await Task.Delay(_retryDelays[attempt], ct);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }
}
