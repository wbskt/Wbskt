namespace Wbskt.Management.Host.Models;

/// <summary>A row of <c>dbo.WorkspaceUsage_GetSummary</c>: the account's totals and the workspace's share.</summary>
/// <param name="TenantCreditsPosted">The part of <c>TenantCredits</c> already in the ledger; the rest is runs' unposted credits.</param>
/// <param name="TenantCreditsDrawn">What the month's posts drew from credit grants because they fell past the allowance.</param>
public sealed record WorkspaceUsageSummary(
    decimal TenantCredits,
    decimal WorkspaceCredits,
    long TenantMessages,
    long WorkspaceMessages,
    int TenantClients,
    int WorkspaceClients,
    int TenantActiveRuns,
    int WorkspaceActiveRuns,
    decimal? MonthlyCreditCap,
    decimal CreditGrantBalance,
    decimal WorkspaceCreditsReserved = 0,
    DateTime? CreditGrantNextExpiresAt = null,
    decimal CreditGrantNextExpiring = 0,
    decimal TenantCreditsPosted = 0,
    decimal TenantCreditsDrawn = 0);
