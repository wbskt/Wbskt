using Microsoft.Extensions.Options;
using Wbskt.Auth.Host.Models;
using Wbskt.Auth.Host.Providers;
using Wbskt.Auth.Host.Services.Email;
using Wbskt.Primitives.Constants;

namespace Wbskt.Auth.Host.Services.Billing;

internal sealed class BillingNotifier(
    ITenantBillingProvider billing,
    IAccessControlProvider access,
    IAuthMailer mailer,
    IOptions<BillingOptions> billingOptions,
    IOptions<AuthEmailOptions> emailOptions,
    ILogger<BillingNotifier> logger) : IBillingNotifier
{
    public async Task NotifyAsync(int tenantId, BillingNotice notice, CancellationToken cancellationToken)
    {
        if (!mailer.IsConfigured)
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

        if (!await billing.ClaimBillingNoticeAsync(tenantId, notice.Kind, notice.PeriodKey, cancellationToken))
        {
            return;
        }

        string billingUrl = billingOptions.Value.ReturnUrl ?? $"{emailOptions.Value.ConsoleBaseUrl.TrimEnd('/')}/workspaces";
        foreach (BillingContact recipient in recipients)
        {
            await mailer.QueueBillingNoticeAsync(recipient.Email, recipient.Username, notice, billingUrl, cancellationToken);
        }

        logger.LogInformation("Told {Recipients} billing admin(s) of tenant {TenantId} about {Notice} ({Period}).", recipients.Count, tenantId, notice.Kind, notice.PeriodKey);
    }
}
