using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Runtime;

/// <summary>
/// A node refused because a charge ran into a budget (#954). Each budget fails the node with its own code
/// and says what to raise; all of them cancel the run with <see cref="CancellationReason"/>, which ends
/// it <c>OutOfCredits</c>.
/// </summary>
public static class OutOfCredits
{
    public const string CancellationReason = "OUT_OF_CREDITS";
    public const string RunBudgetCode = "OUT_OF_CREDITS_RUN_BUDGET";
    public const string PlanCreditsCode = "PLAN_CREDIT_LIMIT_REACHED";
    public const string WorkspaceCapCode = "WORKSPACE_CREDIT_CAP_REACHED";

    /// <summary>Whether a node's error is a credit refusal, including <c>OUT_OF_CREDITS</c> from before the split.</summary>
    public static bool Is(string? errorCode) =>
        errorCode is CancellationReason or RunBudgetCode or PlanCreditsCode or WorkspaceCapCode;

    public static NodeExecutionResult.Fail Fail(CreditRefusal reason) => reason switch
    {
        CreditRefusal.PlanCredits => new(PlanCreditsCode,
            "The account has used this month's workflow credits and its credit packs. Buy a credit pack or move to a larger plan; the allowance starts again on the 1st.",
            false, null),
        CreditRefusal.WorkspaceCap => new(WorkspaceCapCode,
            "The workspace has spent its monthly credit cap. Raise or remove the cap in the workspace's settings.",
            false, null),
        _ => new(RunBudgetCode,
            "The run has used the workflow's credit budget. Raise the workflow's credit budget to let a run go further.",
            false, null)
    };
}
