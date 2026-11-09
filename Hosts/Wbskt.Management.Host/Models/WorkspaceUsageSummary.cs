namespace Wbskt.Management.Host.Models;

/// <summary>A row of <c>dbo.WorkspaceUsage_GetSummary</c>: the account's totals and the workspace's share.</summary>
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
    decimal CreditGrantNextExpiring = 0);
