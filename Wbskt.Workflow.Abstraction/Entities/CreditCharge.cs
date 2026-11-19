namespace Wbskt.Workflow.Abstraction.Entities;

/// <summary>
/// The answer to a charge. Accepted: the run's total spend after it, and the budget. Refused:
/// <see cref="Refusal"/> says which budget ran short, and the amounts are zero.
/// </summary>
public sealed record CreditCharge(decimal Spent, decimal Budget, CreditRefusal? Refusal = null)
{
    public static CreditCharge Refused(CreditRefusal reason) => new(0m, 0m, reason);
}

/// <summary>Which budget a refused charge ran into (<c>dbo.RunCounters_TryCharge</c>'s <c>@Refusal</c>).</summary>
public enum CreditRefusal
{
    /// <summary>The run's own budget: the workflow's credit budget, which a leased run's ceiling is.</summary>
    RunBudget,

    /// <summary>The account's monthly credits, with its packs, are spent.</summary>
    PlanCredits,

    /// <summary>The workspace's monthly credit cap is spent.</summary>
    WorkspaceCap
}
