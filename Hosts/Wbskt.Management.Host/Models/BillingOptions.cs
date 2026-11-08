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
}
