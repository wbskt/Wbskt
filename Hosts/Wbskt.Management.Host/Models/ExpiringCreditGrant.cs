namespace Wbskt.Management.Host.Models;

/// <summary>A credit grant that runs out soon with <see cref="Remaining"/> credits left, and its tenant's workspaces (<c>dbo.TenantCreditGrants_ClaimExpiring</c>).</summary>
public sealed record ExpiringCreditGrant(int TenantId, string Source, string SourceRef, decimal Remaining, DateTime ExpiresAt, IReadOnlyList<int> WorkspaceIds);
