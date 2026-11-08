namespace Wbskt.Workflow.Abstraction.Entities;

/// <summary>
/// Checks a publish makes under its lock. <see cref="ExpectedCurrentVersion"/> refuses it (50025) when
/// the workflow's current version is another (0 for none); <see cref="DraftRevision"/> deletes the
/// draft it was published from, at that revision only. Null skips either.
/// </summary>
public sealed record WorkflowPublishGuards(int? ExpectedCurrentVersion = null, int? DraftRevision = null)
{
    public static readonly WorkflowPublishGuards None = new();
}
