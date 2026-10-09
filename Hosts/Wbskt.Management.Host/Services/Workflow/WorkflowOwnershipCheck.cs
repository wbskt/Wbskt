using Wbskt.Infrastructure;
using Wbskt.Management.Host.Services.Clients;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;

namespace Wbskt.Management.Host.Services.Workflow;

/// <summary>The workspace check both <see cref="WorkflowQueryService"/> and <see cref="WorkflowLifecycleService"/> make before touching a workflow.</summary>
internal static class WorkflowOwnershipCheck
{
    /// <summary>
    /// The current version of a workflow <paramref name="workspaceId"/> owns. Unknown, deleted and
    /// another workspace's workflow all read as <see cref="WorkspaceOwnership.WorkflowNotFound"/>.
    /// </summary>
    public static async Task<Result<WorkflowDefinitionRow>> LoadCurrentAsync(IWorkflowDefinitionProvider provider, ILogger logger, int workspaceId, Guid refId, CancellationToken ct)
    {
        WorkflowDefinitionRow? row = await provider.FindCurrentByRefIdAsync(refId, ct);
        if (row is null)
        {
            return Result<WorkflowDefinitionRow>.Failure(WorkspaceOwnership.WorkflowNotFound);
        }

        var ensureWorkspaceResult = EnsureWorkspace(row, workspaceId, refId, logger);
        return ensureWorkspaceResult.IsFailure
            ? Result<WorkflowDefinitionRow>.Failure(ensureWorkspaceResult.Error)
            : Result<WorkflowDefinitionRow>.Success(row);
    }

    public static Result EnsureWorkspace(WorkflowDefinitionRow row, int workspaceId, Guid refId, ILogger logger)
    {
        if (row.WorkspaceId != workspaceId)
        {
            logger.LogWarning("Workflow '{RefId}' is not in WorkspaceId: {WorkspaceId}", refId, workspaceId);
            return Result.Failure(WorkspaceOwnership.WorkflowNotFound);
        }
        return Result.Success();
    }
}
