namespace Wbskt.Workflow.Abstraction.Runtime;

/// <summary>
/// Told of every trigger the workspace's active-run ceiling refuses, so the user hears about it
/// (#432). The engine host coalesces them into one warning per workflow per window; elsewhere it is a
/// no-op.
/// </summary>
public interface IRunRefusalReporter
{
    void Report(RunRefusal refusal);
}

/// <param name="TriggerKind">The channel the refused event came in on: client, webhook, schedule…</param>
/// <param name="Ceiling">The workspace's limit of active runs, all of them going when it refused.</param>
public sealed record RunRefusal(int WorkspaceId, int WorkflowDefinitionId, Guid WorkflowRefId, string TriggerKind, int Ceiling, DateTime AtUtc);
