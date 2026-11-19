using Wbskt.Auth.Host.Telemetry;
using Wbskt.Infrastructure.Email;

namespace Wbskt.Auth.Host.Services.Email;

/// <summary>
/// Drains <see cref="OutboundMailQueue"/> and hands each message to the relay, off the request path.
/// A message the relay fails to take waits in <see cref="MailRetryList"/> and is tried again on a
/// back-off, while the mail behind it goes out.
/// </summary>
internal sealed class OutboundMailDispatcher : BackgroundService
{
    /// <summary>
    /// Relays fail transiently (greylisting, a connection reset, a restart, an outage of an hour) far
    /// more often than permanently, and some mail, such as the notice that an address was changed, has
    /// no way to be asked for again. So a failed message is tried again for about eight hours, unless
    /// the link in it expires first (<see cref="OutboundMail.ExpiresAtUtc"/>): a password reset is good
    /// for an hour, and one that arrives later only fails.
    /// </summary>
    private static readonly TimeSpan[] DefaultRetryDelays =
    [
        TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(1), TimeSpan.FromHours(2), TimeSpan.FromHours(4)
    ];

    private readonly OutboundMailQueue _queue;
    private readonly MailRetryList _retries = new();
    private readonly IEmailSender _sender;
    private readonly ILogger<OutboundMailDispatcher> _logger;
    private readonly AuthMetrics _metrics;
    private readonly TimeProvider _time;
    private readonly IReadOnlyList<TimeSpan> _retryDelays;

    /// <remarks>
    /// <c>retryDelays</c> is overridable so the suite can exercise the retry ladder without waiting
    /// hours for it. DI never supplies it; production uses <see cref="DefaultRetryDelays"/>.
    /// </remarks>
    public OutboundMailDispatcher(
        OutboundMailQueue queue,
        IEmailSender sender,
        ILogger<OutboundMailDispatcher> logger,
        AuthMetrics metrics,
        TimeProvider time,
        IReadOnlyList<TimeSpan>? retryDelays = null)
    {
        _queue = queue;
        _sender = sender;
        _logger = logger;
        _metrics = metrics;
        _time = time;
        _retryDelays = retryDelays ?? DefaultRetryDelays;
        metrics.ObserveMailRetryDepth(() => _retries.Count);
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

        while (!stoppingToken.IsCancellationRequested)
        {
            if (_retries.TryTakeDue(_time.GetUtcNow(), out var due))
            {
                await SendAsync(due, stoppingToken);
            }
            else if (_queue.TryRead(out var mail))
            {
                await SendAsync(new PendingMail(mail, 0), stoppingToken);
            }
            else if (!await WaitForWorkAsync(stoppingToken))
            {
                return;
            }
        }
    }

    /// <summary>Waits for new mail or the next retry, whichever comes first; false once the queue is closed.</summary>
    private async Task<bool> WaitForWorkAsync(CancellationToken stoppingToken)
    {
        if (_retries.NextDueIn(_time.GetUtcNow()) is not { } nextRetry)
        {
            return await _queue.WaitToReadAsync(stoppingToken);
        }

        using var retryDue = new CancellationTokenSource(nextRetry, _time);
        using var wake = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, retryDue.Token);
        try
        {
            return await _queue.WaitToReadAsync(wake.Token);
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
            return true;
        }
    }

    private async Task SendAsync(PendingMail pending, CancellationToken ct)
    {
        // Never the address, only its fingerprint (EmailFingerprint), and never the subject, which can
        // carry a tenant's name; never the body either, which carries the token.
        var mail = pending.Mail;
        string recipient = EmailFingerprint.Of(mail.To);
        try
        {
            await _sender.SendMultipartAsync(mail.To, mail.Subject, mail.TextBody, mail.HtmlBody, ct);

            // The relay took it; delivery from there is the relay's. Without this line a missing
            // mail cannot be told apart from one that was never handed over.
            _metrics.RecordMail(MailOutcome.Sent);
            _logger.LogInformation("Mail {MailKind} to {RecipientFingerprint} accepted by the relay.", mail.Kind, recipient);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Shutdown. The message is lost with the rest held in memory, and nothing here blocks shutdown.
        }
        catch (Exception ex) when (MailFailure.IsMalformedRecipient(ex, mail.To))
        {
            // Fails identically on every attempt. An operator finds the account by checking a
            // suspect address against the fingerprint.
            _metrics.RecordMail(MailOutcome.InvalidAddress);
            _logger.LogWarning("Mail {MailKind} to {RecipientFingerprint} was not sent: the address is malformed.", mail.Kind, recipient);
        }
        catch (Exception ex) when (MailFailure.IsPermanentRecipientRefusal(ex))
        {
            // The relay will not take this recipient, so trying again cannot help. One bad mailbox is
            // not the relay failing, so it is counted apart and does not alert.
            _metrics.RecordMail(MailOutcome.Rejected);
            _logger.LogWarning("Mail {MailKind} to {RecipientFingerprint} was refused by the relay: {Failure}.", mail.Kind, recipient, MailFailure.Describe(ex));
        }
        catch (Exception ex)
        {
            Retry(pending, recipient, ex);
        }
    }

    private void Retry(PendingMail pending, string recipient, Exception failure)
    {
        var (mail, attempts) = (pending.Mail, pending.Attempts + 1);
        if (attempts > _retryDelays.Count)
        {
            _metrics.RecordMail(MailOutcome.Dropped);
            _logger.LogError("Mail {MailKind} to {RecipientFingerprint} was not sent after {Attempts} attempts: {Failure}.", mail.Kind, recipient, attempts, MailFailure.Describe(failure));
            return;
        }

        var delay = _retryDelays[attempts - 1];
        var dueAt = _time.GetUtcNow() + delay;
        if (mail.ExpiresAtUtc is { } expiresAt && dueAt >= expiresAt)
        {
            // A reset or invitation link arriving after it stopped working only confuses; the person
            // asks for a new one, which is what they would have to do with this one anyway.
            _metrics.RecordMail(MailOutcome.Expired);
            _logger.LogWarning("Mail {MailKind} to {RecipientFingerprint} was not sent: its link expires before attempt {Attempt} is due. {Failure}.", mail.Kind, recipient, attempts + 1, MailFailure.Describe(failure));
            return;
        }

        if (!_retries.TryAdd(pending with { Attempts = attempts }, dueAt))
        {
            _metrics.RecordMail(MailOutcome.Dropped);
            _logger.LogError("Mail {MailKind} to {RecipientFingerprint} was not sent: {Waiting} messages already wait to be tried again. {Failure}.", mail.Kind, recipient, MailRetryList.Capacity, MailFailure.Describe(failure));
            return;
        }

        _metrics.RecordMail(MailOutcome.Retried);
        _logger.LogWarning("Mail {MailKind} to {RecipientFingerprint} failed on attempt {Attempt}; trying again in {Delay}: {Failure}.", mail.Kind, recipient, attempts, delay, MailFailure.Describe(failure));
    }
}
