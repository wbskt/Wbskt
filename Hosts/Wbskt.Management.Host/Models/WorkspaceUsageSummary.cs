namespace Wbskt.Management.Host.Models;

/// <summary>A row of <c>dbo.WorkspaceUsage_GetSummary</c>: the account's totals and the workspace's share.</summary>
public sealed record WorkspaceUsageSummary(
    decimal TenantCredits,
    decimal WorkspaceCredits,
    int TenantClients,
    int WorkspaceClients,
    int TenantActiveRuns,
    int WorkspaceActiveRuns,
    decimal? MonthlyCreditCap);
