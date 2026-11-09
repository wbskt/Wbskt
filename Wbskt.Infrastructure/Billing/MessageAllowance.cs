using Wbskt.Models.Billing;

namespace Wbskt.Infrastructure.Billing;

/// <summary>
/// What happens to an account's client messages once it has used its plan's monthly allowance
/// (Docs/Billing.md, Messages). A Free account is throttled: each of its clients may send one message
/// every <see cref="ThrottledInterval"/> until the month ends. A paid account is never throttled; what it
/// carries past the limit is overage.
/// </summary>
public static class MessageAllowance
{
    public static readonly TimeSpan ThrottledInterval = TimeSpan.FromMinutes(1);

    public static bool IsThrottled(PlanCode plan, long messagesUsed, long messagesPerMonth) =>
        plan == PlanCode.Free && messagesUsed >= messagesPerMonth;
}
