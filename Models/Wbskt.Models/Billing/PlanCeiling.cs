namespace Wbskt.Models.Billing;

/// <summary>
/// A plan limit as the database enforces it: <see cref="Limit"/> counted across every workspace of
/// <see cref="TenantId"/> (<c>dbo.TenantWorkspaces</c>), or across the one workspace while its tenant is
/// not known yet.
/// </summary>
public sealed record PlanCeiling(int Limit, int? TenantId);
