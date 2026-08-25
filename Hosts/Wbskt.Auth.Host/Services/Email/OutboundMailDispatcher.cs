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
    private readonly IReadOnlyList<TimeSpan> _retryDelays;

    /// <remarks>
    /// <c>retryDelays</c> is overridable so the suite can exercise the retry ladder without waiting
    /// ten seconds for it. DI never supplies it; production uses <see cref="DefaultRetryDelays"/>.
    /// </remarks>
    public OutboundMailDispatcher(
        OutboundMailQueue queue,
        IEmailSender sender,
        ILogger<OutboundMailDispatcher> logger,
        IReadOnlyList<TimeSpan>? retryDelays = null)
    {
        _queue = queue;
        _sender = sender;
        _logger = logger;
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
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                await _sender.SendAsync(mail.To, mail.Subject, mail.HtmlBody, EmailBodyFormat.Html, ct);
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
                _logger.LogWarning(ex, "Mail to {Recipient} was not sent: the address is malformed.", mail.To);
                return;
            }
            catch (Exception ex)
            {
                if (attempt >= _retryDelays.Count)
                {
                    _logger.LogError(ex, "Mail to {Recipient} was not sent after {Attempts} attempts.", mail.To, attempt + 1);
                    return;
                }

                _logger.LogWarning(ex, "Mail to {Recipient} failed on attempt {Attempt}; retrying.", mail.To, attempt + 1);

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
