using Wbskt.Models.Billing;

namespace Wbskt.Infrastructure.Billing;

/// <summary>
/// What happens to an account's client messages once it has used its plan's monthly allowance
/// (Docs/Billing.md, Messages). A throttled account's clients may each send one message every
/// <see cref="ThrottledInterval"/> until the month ends. Free is throttled at its allowance; Maker, Pro and Team
/// carry on past theirs with no overage charge, and are throttled the same way only at
/// <c>Billing:PaidMessageThrottleMultiple</c> times it. Business is never throttled.
/// </summary>
public static class MessageAllowance
{
    public static readonly TimeSpan ThrottledInterval = TimeSpan.FromMinutes(1);

    public const decimal DefaultPaidThrottleMultiple = 2m;

    /// <summary>The share of the allowance at which the account is first warned.</summary>
    public const decimal WarningShare = 0.8m;

    public static bool IsThrottled(PlanCode plan, long messagesUsed, long messagesPerMonth, decimal paidThrottleMultiple) =>
        ThrottleAt(plan, messagesPerMonth, paidThrottleMultiple) is { } at && messagesUsed >= at;

    /// <summary>The messages a month at which the plan is throttled, or null for a plan that never is.</summary>
    public static decimal? ThrottleAt(PlanCode plan, long messagesPerMonth, decimal paidThrottleMultiple) => plan switch
    {
        PlanCode.Free => messagesPerMonth,
        PlanCode.Business => null,
        _ => messagesPerMonth * paidThrottleMultiple
    };
}
