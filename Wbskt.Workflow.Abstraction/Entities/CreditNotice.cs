using Wbskt.Workflow.Abstraction.Enums;

namespace Wbskt.Workflow.Abstraction.Entities;

/// <summary>
/// A workspace's cap, or a tenant's plan allowance, that its spend this month has reached past what it was
/// told (<c>dbo.CreditNotices_GetDue</c>). Each threshold above <see cref="Notified"/> up to <see cref="Level"/>
/// is owed a notice.
/// </summary>
/// <param name="WorkspaceId">The capped workspace, or for an allowance the tenant's workspace that posted the most.</param>
/// <param name="TenantId">The tenant, for an allowance; null for a cap.</param>
/// <param name="Limit">The cap or the allowance.</param>
/// <param name="Spent">What is posted against it this month.</param>
/// <param name="Level">The highest threshold reached: 80 or 100.</param>
/// <param name="Notified">The highest threshold already announced: 0, 80 or 100.</param>
public sealed record CreditNotice(CreditNoticeKind Kind, int WorkspaceId, int? TenantId, decimal Limit, decimal Spent, int Level, int Notified)
{
    private static readonly int[] Thresholds = [80, 100];

    /// <summary>The thresholds owed a notice, lowest first.</summary>
    public IReadOnlyCollection<int> Owed() => Thresholds.Where(percent => percent > Notified && percent <= Level).ToArray();
}
