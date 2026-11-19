namespace Wbskt.Workflow.Abstraction.Entities;

/// <summary>
/// An Active branch as recovery sees it: enough to dispatch it and to re-apply its run's stop, without
/// the branch's local state (<c>dbo.Branch_GetRunning</c>).
/// </summary>
public sealed record RunningBranch(long Id, int RunId, DateTime UpdatedAt, string RunStatus, string? RunCancellationReason);
