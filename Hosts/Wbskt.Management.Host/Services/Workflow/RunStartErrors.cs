using Wbskt.Infrastructure;

namespace Wbskt.Management.Host.Services.Workflow;

/// <summary>Why the engine started no run, told the same way for a manual run and a test run.</summary>
internal static class RunStartErrors
{
    public static readonly Error PlanRunLimitReached = Error.Conflict(
        "PLAN_RUN_LIMIT_REACHED",
        "The plan is at its limit of runs going at once. Try again when one finishes.");

    public static readonly Error PlanCreditLimitReached = Error.Conflict(
        "PLAN_CREDIT_LIMIT_REACHED",
        "The plan has used its workflow credits for this month. Runs start again on the 1st, or after an upgrade.");

    public static readonly Error WorkspaceCreditCapReached = Error.Conflict(
        "WORKSPACE_CREDIT_CAP_REACHED",
        "This workspace has used its monthly credit cap. Runs start again on the 1st, or when the cap is raised.");

    public static readonly Error WorkspaceBusy = Error.Unavailable(
        "WORKSPACE_BUSY",
        "Too many runs are starting in this workspace at once. Try again in a moment.");
}
