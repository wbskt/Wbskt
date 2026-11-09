namespace Wbskt.Workflow.Abstraction.Entities;

/// <summary>What one post (<c>dbo.RunCredits_Post</c>) moved into a workspace's credit ledger.</summary>
/// <param name="Runs">How many of the workspace's runs posted.</param>
/// <param name="Credits">How many credits they moved.</param>
/// <param name="SpentThisMonth">What the ledger holds for the workspace this month, with them.</param>
/// <param name="MonthlyCreditCap">The workspace's credit cap; null for none.</param>
public sealed record WorkspaceCreditPost(int WorkspaceId, int Runs, decimal Credits, decimal SpentThisMonth, decimal? MonthlyCreditCap)
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

    private static readonly int[] CapThresholds = [80, 100];
}
