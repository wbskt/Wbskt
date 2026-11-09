using Microsoft.Extensions.Options;
using Wbskt.Auth.Host.Models;
using Wbskt.Auth.Host.Providers;
using Wbskt.Auth.Host.Services.Email;
using Wbskt.Infrastructure.Email;
using Wbskt.Primitives.Constants;

namespace Wbskt.Auth.Host.Services.Billing;

/// <summary>
/// Renders a notice for each billing admin and records it with its claim in one transaction
/// (<c>dbo.BillingNotice_Record</c>); <see cref="BillingNoticeMailSender"/> hands the emails to the relay.
/// Not the in-memory mail queue: a notice is sent once, so one lost to a restart or a refusing relay
/// would never be sent at all.
/// </summary>
internal sealed class BillingNotifier(
    ITenantBillingProvider billing,
    IAccessControlProvider access,
    IEmailSender sender,
    IOptions<BillingOptions> billingOptions,
    IOptions<AuthEmailOptions> emailOptions,
    ILogger<BillingNotifier> logger) : IBillingNotifier
{
    public async Task NotifyAsync(int tenantId, BillingNotice notice, CancellationToken cancellationToken)
    {
        if (!sender.IsConfigured)
        {
            logger.LogWarning("No SMTP relay is configured, so tenant {TenantId} was not told about {Notice} ({Period}).", tenantId, notice.Kind, notice.PeriodKey);
            return;
        }

        var recipients = new List<BillingContact>();
        foreach (BillingContact contact in await billing.GetBillingContactsAsync(tenantId, cancellationToken))
        {
            if (await access.VerifyPermissionAsync(contact.UserId, tenantId, null, PermissionNames.BillingManage, cancellationToken))
            {
                recipients.Add(contact);
            }
        }

        if (recipients.Count == 0)
        {
            logger.LogWarning("Tenant {TenantId} has no member with a confirmed address who manages billing, so it was not told about {Notice} ({Period}).", tenantId, notice.Kind, notice.PeriodKey);
            return;
        }

        AuthEmailOptions options = emailOptions.Value;
        string billingUrl = billingOptions.Value.ReturnUrl ?? $"{options.ConsoleBaseUrl.TrimEnd('/')}/workspaces";
        OutboundMail[] mails = recipients.Select(r => AuthEmailTemplates.Billing(options, r.Email, r.Username, notice, billingUrl)).ToArray();
        if (await billing.RecordBillingNoticeAsync(tenantId, notice.Kind, notice.PeriodKey, mails, cancellationToken))
        {
            logger.LogInformation("Queued {Notice} ({Period}) for {Recipients} billing admin(s) of tenant {TenantId}.", notice.Kind, notice.PeriodKey, mails.Length, tenantId);
        }
    }
}
