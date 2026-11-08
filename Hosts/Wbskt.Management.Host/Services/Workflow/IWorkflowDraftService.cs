using Wbskt.Infrastructure;
using Wbskt.Management.Models.Workflow;

namespace Wbskt.Management.Host.Services.Workflow;

/// <summary>A workflow's unpublished changes, kept on the server: at most one draft per workflow.</summary>
public interface IWorkflowDraftService
{
    Task<IReadOnlyList<WorkflowDraftSummaryDto>> ListAsync(int workspaceId, CancellationToken ct);

    Task<Result<WorkflowDraftDto>> GetAsync(int workspaceId, Guid refId, CancellationToken ct);

    /// <summary>Starts or updates the draft. An invalid definition is saved; its problems come back with it.</summary>
    Task<Result<WorkflowDraftDto>> SaveAsync(int workspaceId, Guid refId, WorkflowDraftSaveRequest request, CancellationToken ct);

    /// <summary>Discards the draft, only at <paramref name="revision"/> when given.</summary>
    Task<Result> DiscardAsync(int workspaceId, Guid refId, int? revision, CancellationToken ct);

    /// <summary>Publishes the draft as the workflow's next version and deletes it.</summary>
    Task<Result<WorkflowPublishResponse>> PublishAsync(int workspaceId, Guid workspaceRef, Guid refId, WorkflowDraftPublishRequest request, CancellationToken ct);
}
