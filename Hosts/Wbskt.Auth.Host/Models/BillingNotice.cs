using Wbskt.Models.Billing;

namespace Wbskt.Auth.Host.Models;

/// <summary>What a billing notice is about. Stored by name in <c>dbo.BillingNotices</c>, so a name never changes.</summary>
public enum BillingNoticeKind
{
    CreditAllowance80,
    CreditAllowance100,
    CreditCap80,
    CreditCap100,
    Messages80,
    Messages100,
    MessagesThrottled,
    PaymentFailed,
    GraceEnding,
    Downgraded,
    PackExpiring
}

/// <summary>
/// One email to a tenant's billing admins, sent once per <see cref="Kind"/> and <see cref="PeriodKey"/>
/// (a month, a payment, a plan revision). <see cref="Paragraphs"/> are plain text: the template encodes them.
/// </summary>
public sealed record BillingNotice(BillingNoticeKind Kind, string PeriodKey, string Subject, string Heading, IReadOnlyList<string> Paragraphs);

/// <summary>A tenant member who can be written to (<c>dbo.TenantMember_GetContacts</c>).</summary>
public sealed record BillingContact(int UserId, string Username, string Email);

/// <summary>A subscription behind on payment (<c>dbo.TenantSubscriptions_GetPastDueBetween</c>).</summary>
public sealed record PastDueSubscription(int TenantId, string SubscriptionId, PlanCode PlanCode, DateTime PastDueSince);

/// <summary>A credit pack that still counts and runs out soon (<c>dbo.TenantCreditPurchases_GetExpiring</c>).</summary>
public sealed record ExpiringCreditPurchase(int TenantId, string PaymentId, decimal Credits, DateTime ExpiresAt);

/// <summary>A billing email leased for sending (<c>dbo.BillingNoticeMails_Take</c>); <see cref="Attempts"/> counts this one.</summary>
public sealed record BillingNoticeMail(long Id, int TenantId, BillingNoticeKind Kind, string To, string Subject, string HtmlBody, int Attempts);
