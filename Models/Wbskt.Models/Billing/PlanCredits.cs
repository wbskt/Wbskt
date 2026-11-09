namespace Wbskt.Models.Billing;

/// <summary>
/// A plan's workflow credits for a calendar month (UTC) as the database enforces them: spent across every
/// workspace of <see cref="TenantId"/> (<c>dbo.TenantWorkspaces</c>), or by the one workspace while its
/// tenant is not known yet.
/// </summary>
public sealed record PlanCredits(decimal MonthlyAllowance, int? TenantId);
