using Wbskt.Auth.Host.Services.Email;
using Wbskt.Auth.Host.Telemetry;
using Wbskt.Auth.Host.Models;
using Wbskt.Auth.Host.Providers;
using Wbskt.Infrastructure.Email;
using Wbskt.Infrastructure.Hosting;

namespace Wbskt.Auth.Host.Services.Billing;

/// <summary>
/// Every minute, hands the billing emails that are due (<c>dbo.BillingNoticeMails</c>) to the relay. Each
/// is leased while it is sent, so two auth instances never both send it, and one the relay refuses is
/// tried again later, further apart each time, until <see cref="MaxAttempts"/>. Sent ones are removed
/// after <see cref="KeepFor"/>.
/// </summary>
public sealed class BillingNoticeMailSender(IServiceScopeFactory scopeFactory, TimeProvider clock, ILogger<BillingNoticeMailSender> logger)
    : PeriodicWorker(AlwaysOpenWorkGate.Instance, scopeFactory, logger, clock)
{
    internal const int BatchSize = 50;

    internal const int MaxAttempts = 6;

    internal static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);

    // Longer than one send may take, so a send started before it is left always finishes inside the
    // lease. The auth host refuses to start with an EmailOptions.SendTimeout that is not shorter.
    internal static readonly TimeSpan LeaseReserve = TimeSpan.FromMinutes(1);

    internal static readonly TimeSpan KeepFor = TimeSpan.FromDays(30);

    // After the 1st, 2nd, ... failed attempt; the 6th failure gives up, about a day after the first.
    private static readonly TimeSpan[] Backoff = [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30), TimeSpan.FromHours(2), TimeSpan.FromHours(12)];

    private readonly TimeProvider _clock = clock;

    protected override string Name => "Billing notice mail";

    protected override TimeSpan Interval { get; } = TimeSpan.FromMinutes(1);

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken ct)
    {
        var sender = services.GetRequiredService<IEmailSender>();
        if (!sender.IsConfigured)
        {
            return;
        }

        var billing = services.GetRequiredService<ITenantBillingProvider>();
        DateTime now = _clock.GetUtcNow().UtcDateTime;
        DateTime leaseUntil = now + Lease;
        IReadOnlyCollection<BillingNoticeMail> batch = await billing.TakeBillingNoticeMailsAsync(now, leaseUntil, BatchSize, ct);
        int sent = 0;
        foreach (BillingNoticeMail mail in batch)
        {
            // A slow relay can use up the lease before the batch is done. Past the point where one more
            // send could outlive it, the rest are handed back untried, so no other instance can send one
            // at the same time and none of them loses the attempt the lease counted.
            if (_clock.GetUtcNow().UtcDateTime + LeaseReserve >= leaseUntil)
            {
                Logger.LogWarning("The relay is slow: stopped this billing mail batch before its lease ran out and handed back {Count} mail(s).", batch.Count - sent);
                foreach (BillingNoticeMail untried in batch.Skip(sent))
                {
                    await billing.ReleaseBillingNoticeMailAsync(untried.Id, leaseUntil, _clock.GetUtcNow().UtcDateTime, CancellationToken.None);
                }

                break;
            }

            await SendAsync(sender, billing, mail, ct);
            sent++;
        }

        await billing.PurgeBillingNoticeMailsAsync(now - KeepFor, ct);
    }

    private async Task SendAsync(IEmailSender sender, ITenantBillingProvider billing, BillingNoticeMail mail, CancellationToken ct)
    {
        try
        {
            if (mail.TextBody is null)
            {
                await sender.SendAsync(mail.To, mail.Subject, mail.HtmlBody, EmailBodyFormat.Html, ct);
            }
            else
            {
                await sender.SendMultipartAsync(mail.To, mail.Subject, mail.TextBody, mail.HtmlBody, ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            DateTime now = _clock.GetUtcNow().UtcDateTime;

            // A malformed address, or a recipient the relay refuses for good, fails the same way every time.
            bool giveUp = MailFailure.IsMalformedRecipient(ex, mail.To) || MailFailure.IsPermanentRecipientRefusal(ex) || mail.Attempts >= MaxAttempts;
            if (giveUp)
            {
                Logger.LogError("Gave up on the {Notice} email to {RecipientFingerprint} of tenant {TenantId} after {Attempts} attempt(s): {Failure}.", mail.Kind, EmailFingerprint.Of(mail.To), mail.TenantId, mail.Attempts, MailFailure.Describe(ex));
            }
            else
            {
                Logger.LogWarning("The relay refused the {Notice} email to {RecipientFingerprint} of tenant {TenantId}; trying again: {Failure}.", mail.Kind, EmailFingerprint.Of(mail.To), mail.TenantId, MailFailure.Describe(ex));
            }

            await billing.CompleteBillingNoticeMailAsync(
                mail.Id, sentAt: null, retryAt: giveUp ? null : now + Backoff[Math.Min(mail.Attempts, Backoff.Length) - 1], gaveUpAt: giveUp ? now : null, MailFailure.Describe(ex), CancellationToken.None);
            return;
        }

        await billing.CompleteBillingNoticeMailAsync(mail.Id, _clock.GetUtcNow().UtcDateTime, retryAt: null, gaveUpAt: null, error: null, CancellationToken.None);
        Logger.LogInformation("The {Notice} email to {RecipientFingerprint} of tenant {TenantId} was accepted by the relay.", mail.Kind, EmailFingerprint.Of(mail.To), mail.TenantId);
    }
}
