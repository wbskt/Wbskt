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
}
