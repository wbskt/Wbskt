using Wbskt.Models.Billing;
using Wbskt.Infrastructure.Billing;
namespace Wbskt.Management.Host.Models;

/// <summary>The management host's part in billing.</summary>
public sealed class BillingOptions
{
    public const string SectionName = "Billing";

    /// <summary>
    /// How often every workspace account is copied from the auth host (<c>WorkspaceAccountSyncWorker</c>).
    /// Events keep the copies current in seconds; this only bounds how long one an event missed stays wrong.
    /// </summary>
    public TimeSpan AccountSyncInterval { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// How often each account's client messages are compared with its plan (<c>MessageAllowanceWorker</c>),
    /// which is also how often the socket hosts hear which workspaces to throttle. A Free account can go
    /// about this far past its allowance before its clients are slowed.
    /// </summary>
    public TimeSpan MessageAllowanceInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// How often every credit pack bought is compared with the main database's grants
    /// (<c>CreditGrantReconcileWorker</c>). Events keep the grants current in seconds; this only bounds how
    /// long a grant an event missed stays wrong.
    /// </summary>
    public TimeSpan CreditGrantReconcileInterval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// How often the tenants on Business now are recorded against the month (<c>BusinessUsageWorker</c>),
    /// so the month's overage report still lists one whose contract ends before the month is closed.
    /// </summary>
    public TimeSpan BusinessUsageInterval { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// How long after a month ends its Business usage is closed and kept for the overage report. Client
    /// messages are counted on the day they were sent, so ones the meter takes in late (after an outage or a
    /// backlog) still land in the month until it closes; after that they are left off its invoice. Until
    /// then the report works the month out as it stands.
    /// </summary>
    public TimeSpan BusinessMonthCloseDelay { get; set; } = TimeSpan.FromDays(1);

    /// <summary>
    /// How many times its monthly message allowance a Maker, Pro or Team account may carry before its clients
    /// are held to Free's one message a minute until the 1st, by plan
    /// (<c>Billing:PaidMessageThrottleMultiples:Team</c>). Paid plans are never charged for overage, so this
    /// only stops one account from loading the hosts without end. A plan left out keeps its default
    /// (<see cref="MessageAllowance.DefaultPaidThrottleMultiples"/>); each must be more than 1 and name a paid
    /// self-serve plan (checked at start). Free is throttled at 1x; Business never is.
    /// </summary>
    public Dictionary<PlanCode, decimal> PaidMessageThrottleMultiples { get; set; } = new(MessageAllowance.DefaultPaidThrottleMultiples);

    /// <summary>The multiple <paramref name="plan"/> is throttled at; 1 for Free and Business, which ignore it.</summary>
    public decimal PaidMessageThrottleMultipleFor(PlanCode plan) =>
        PaidMessageThrottleMultiples.TryGetValue(plan, out decimal multiple) ? multiple : 1m;

    /// <summary>True when every multiple names a plan that has one and is more than 1.</summary>
    public bool PaidMessageThrottleMultiplesAreValid() =>
        PaidMessageThrottleMultiples.All(p => MessageAllowance.DefaultPaidThrottleMultiples.ContainsKey(p.Key) && p.Value > 1);
}
