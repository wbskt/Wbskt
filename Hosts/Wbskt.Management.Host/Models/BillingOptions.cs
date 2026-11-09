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
    /// How many times its monthly message allowance a Maker, Pro or Team account may carry before its clients
    /// are held to Free's one message a minute until the 1st. Paid plans are never charged for overage, so
    /// this only stops one account from loading the hosts without end. Must be more than 1 (checked at
    /// start). Free is throttled at 1x; Business never is.
    /// </summary>
    public decimal PaidMessageThrottleMultiple { get; set; } = Wbskt.Infrastructure.Billing.MessageAllowance.DefaultPaidThrottleMultiple;
}
