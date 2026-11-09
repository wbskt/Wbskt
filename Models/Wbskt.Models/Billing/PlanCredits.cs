namespace Wbskt.Models.Billing;

/// <summary>
/// A plan's workflow credits for a calendar month (UTC) as the database enforces them: spent across every
/// workspace of <see cref="TenantId"/> (<c>dbo.TenantWorkspaces</c>), or by the one workspace while its
/// tenant is not known yet.
/// </summary>
/// <param name="MonthlyAllowance">The credits the plan includes, which notices and usage measure against.</param>
public sealed record PlanCredits(decimal MonthlyAllowance, int? TenantId)
{
    /// <summary>
    /// Where new runs are refused: the allowance itself, except on Business, whose runs carry on past it
    /// to <see cref="BusinessContract.CreditOverageMultiple"/> times it and are invoiced for the overage.
    /// </summary>
    public decimal RunsRefusedAt { get; init; } = MonthlyAllowance;
}
