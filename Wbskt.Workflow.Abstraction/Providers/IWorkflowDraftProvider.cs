using Wbskt.Workflow.Abstraction.Entities;

namespace Wbskt.Workflow.Abstraction.Providers;

public interface IWorkflowDraftProvider
{
    /// <summary>The draft of <paramref name="refId"/> in <paramref name="workspaceId"/>, or null.</summary>
    Task<WorkflowDraftRow?> FindAsync(Guid refId, int workspaceId, CancellationToken ct);

    /// <summary>Every draft in the workspace, without definitions, newest edit first.</summary>
    Task<IReadOnlyCollection<WorkflowDraftRow>> ListAsync(int workspaceId, CancellationToken ct);

    /// <summary>
    /// Creates (<paramref name="expectedRevision"/> null) or updates a draft. Throws <c>SqlException</c>
    /// 50021 when the workflow is another workspace's, 50022 when it was deleted, 50031 when the draft's
    /// revision is not <paramref name="expectedRevision"/>, and 50032 when a new workflow's draft would
    /// pass <paramref name="maxDrafts"/>.
    /// </summary>
    Task<WorkflowDraftRow> SaveAsync(WorkflowDraftSave save, int? expectedRevision, int? maxDrafts, CancellationToken ct);

    /// <summary>Discards the draft, only at <paramref name="expectedRevision"/> when given. True when one was removed.</summary>
    Task<bool> DeleteAsync(Guid refId, int workspaceId, int? expectedRevision, CancellationToken ct);
}
