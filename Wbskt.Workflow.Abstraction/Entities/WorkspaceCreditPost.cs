namespace Wbskt.Workflow.Abstraction.Entities;

/// <summary>What one post (<c>dbo.RunCredits_Post</c>) moved into a workspace's credit ledger.</summary>
/// <param name="Runs">How many of the workspace's runs posted.</param>
/// <param name="Credits">How many credits they moved.</param>
/// <param name="SpentThisMonth">What the ledger holds for the workspace this month, with them.</param>
/// <param name="MonthlyCreditCap">The workspace's credit cap; null for none.</param>
/// <param name="TenantId">The workspace's tenant; null while the workspace has no copy of its account.</param>
/// <param name="CreditsPerMonth">The tenant's plan's monthly allowance; null for none.</param>
/// <param name="TenantSpentThisMonth">What the tenant has posted this month, with this post.</param>
public sealed record WorkspaceCreditPost(
    int WorkspaceId,
    int Runs,
    decimal Credits,
    decimal SpentThisMonth,
    decimal? MonthlyCreditCap,
    int? TenantId = null,
    decimal? CreditsPerMonth = null,
    decimal? TenantSpentThisMonth = null)
{
    /// <summary>The share of its cap, out of 80 and 100, that this post carried the workspace up to or past.</summary>
    public IReadOnlyCollection<int> CapThresholdsCrossed()
    {
        if (MonthlyCreditCap is not { } cap || Credits <= 0)
        {
            return [];
        }

        decimal before = SpentThisMonth - Credits;
        return CapThresholds.Where(percent => before < cap * percent / 100 && SpentThisMonth >= cap * percent / 100).ToArray();
    }

    /// <summary>
    /// For each tenant in one post, the share of its plan's monthly allowance, out of 80 and 100, that the
    /// post carried it up to or past, with the workspace that posted the most of it.
    /// </summary>
    public static IReadOnlyCollection<TenantAllowanceCrossing> AllowanceThresholdsCrossed(IEnumerable<WorkspaceCreditPost> posts)
    {
        var crossed = new List<TenantAllowanceCrossing>();
        foreach (var tenant in posts.Where(p => p is { TenantId: not null, CreditsPerMonth: > 0, TenantSpentThisMonth: not null }).GroupBy(p => p.TenantId!.Value))
        {
            decimal allowance = tenant.First().CreditsPerMonth!.Value;
            decimal after = tenant.First().TenantSpentThisMonth!.Value;
            decimal before = after - tenant.Sum(p => p.Credits);
            int workspaceId = tenant.MaxBy(p => p.Credits)!.WorkspaceId;
            foreach (int percent in CapThresholds.Where(percent => before < allowance * percent / 100 && after >= allowance * percent / 100))
            {
                crossed.Add(new TenantAllowanceCrossing(tenant.Key, workspaceId, percent, allowance, after));
            }
        }

        return crossed;
    }

    private static readonly int[] CapThresholds = [80, 100];
}

/// <summary>A tenant carried to <see cref="Percent"/> of its plan's monthly credits by one post, led by <see cref="WorkspaceId"/>.</summary>
public sealed record TenantAllowanceCrossing(int TenantId, int WorkspaceId, int Percent, decimal Allowance, decimal Spent);
