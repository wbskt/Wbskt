using Wbskt.Models.Billing;

namespace Wbskt.Workflow.Abstraction.Runtime;

/// <summary>
/// Told of every trigger a plan limit refuses (active runs or monthly credits), so the user hears about it
/// (#432). The engine host coalesces them into one warning per workflow per window; elsewhere it is a
/// no-op.
/// </summary>
public interface IRunRefusalReporter
{
    void Report(RunRefusal refusal);
}

/// <param name="TriggerKind">The channel the refused event came in on: client, webhook, schedule…</param>
/// <param name="Ceiling">The limit that refused it: active runs, or the month's credits in whole credits.</param>
public sealed record RunRefusal(int WorkspaceId, int WorkflowDefinitionId, Guid WorkflowRefId, string TriggerKind, int Ceiling, DateTime AtUtc)
{
    public RunRefusalReason Reason { get; init; } = RunRefusalReason.ActiveRunLimit;
}
