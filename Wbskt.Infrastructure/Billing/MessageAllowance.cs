using Wbskt.Models.Billing;

namespace Wbskt.Infrastructure.Billing;

/// <summary>
/// What happens to an account's client messages once it has used its plan's monthly allowance
/// (Docs/Billing.md, Messages). A throttled account's clients may each send one message every
/// <see cref="ThrottledInterval"/> until the month ends. Free is throttled at its allowance; Maker, Pro and Team
/// carry on past theirs with no overage charge, and are throttled the same way only at their plan's multiple
/// of it (<see cref="DefaultPaidThrottleMultiples"/>, <c>Billing:PaidMessageThrottleMultiples</c>). Business is
/// never throttled.
/// </summary>
public static class MessageAllowance
{
    public static readonly TimeSpan ThrottledInterval = TimeSpan.FromMinutes(1);

    /// <summary>
    /// How far past its allowance each paid plan carries before it is throttled. Team's is lower: one Team
    /// account at 2x (154 msg/s) is past the single-VM database trigger on its own, while Maker and Pro at 2x
    /// are a small share of it (#751; the capacity estimate and hosting plan in the project files).
    /// </summary>
    public static readonly IReadOnlyDictionary<PlanCode, decimal> DefaultPaidThrottleMultiples = new Dictionary<PlanCode, decimal>
    {
        [PlanCode.Maker] = 2m,
        [PlanCode.Pro] = 2m,
        [PlanCode.Team] = 1.5m
    };

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
