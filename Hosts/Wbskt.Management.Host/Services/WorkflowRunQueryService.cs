using Wbskt.Management.Models.Workflow;
using Wbskt.Primitives.Exceptions;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Management.Host.Services;

public sealed class WorkflowRunQueryService : IWorkflowRunQueryService
{
    private readonly IRunProvider _runProvider;
    private readonly IBranchProvider _branchProvider;
    private readonly IWorkflowDefinitionProvider _workflowDefinitionProvider;
    private readonly IRunCancellationService _cancellationService;

    public WorkflowRunQueryService(
        IRunProvider runProvider,
        IBranchProvider branchProvider,
        IWorkflowDefinitionProvider workflowDefinitionProvider,
        IRunCancellationService cancellationService)
    {
        _runProvider = runProvider;
        _branchProvider = branchProvider;
        _workflowDefinitionProvider = workflowDefinitionProvider;
        _cancellationService = cancellationService;
    }

    public async Task<RunListResponse> ListByWorkflowAsync(int workspaceId, Guid workflowRefId, string? statusFilter, int top, long? cursorId, CancellationToken ct)
    {
        await EnsureWorkflowInWorkspaceAsync(workspaceId, workflowRefId, ct);
        IReadOnlyCollection<RunRow> rows = await _runProvider.ListByWorkflowAsync(workflowRefId, statusFilter, top, cursorId, ct);
        IReadOnlyList<RunSummaryDto> runs = rows.Select(MapRun).ToList();
        long? nextCursor = runs.Count == top ? rows.Last().Id : null;
        return new RunListResponse(runs, nextCursor);
    }

    public async Task<RunDetailDto> GetDetailAsync(int workspaceId, Guid runRefId, CancellationToken ct)
    {
        RunRow run = await _runProvider.GetByRefIdAsync(runRefId, ct);
        await EnsureRunRowInWorkspaceAsync(workspaceId, run, ct);
        IReadOnlyCollection<BranchRow> branches = await _branchProvider.GetAllByRunIdAsync(run.Id, ct);
        return new RunDetailDto(MapRun(run), branches.Select(MapBranch).ToList());
    }

    public async Task CancelAsync(int workspaceId, Guid runRefId, string reason, CancellationToken ct)
    {
        int runId = await EnsureRunInWorkspaceAsync(workspaceId, runRefId, ct);
        await _cancellationService.RequestCancellationAsync(runId, reason, ct);
    }

    public async Task<int> EnsureRunInWorkspaceAsync(int workspaceId, Guid runRefId, CancellationToken ct)
    {
        RunRow run = await _runProvider.GetByRefIdAsync(runRefId, ct);
        await EnsureRunRowInWorkspaceAsync(workspaceId, run, ct);
        return run.Id;
    }

    private async Task EnsureRunRowInWorkspaceAsync(int workspaceId, RunRow run, CancellationToken ct)
    {
        WorkflowDefinitionRow definition = await _workflowDefinitionProvider.GetByRefIdVersionAsync(run.WorkflowRefId, run.WorkflowVersion, ct);
        if (definition.WorkspaceId != workspaceId)
        {
            throw new SecurityException($"Run '{run.RefId}' does not belong to the workspace.");
        }
    }

    private async Task EnsureWorkflowInWorkspaceAsync(int workspaceId, Guid workflowRefId, CancellationToken ct)
    {
        WorkflowDefinitionRow definition = await _workflowDefinitionProvider.GetCurrentByRefIdAsync(workflowRefId, ct);
        if (definition.WorkspaceId != workspaceId)
        {
            throw new SecurityException($"Workflow '{workflowRefId}' does not belong to the workspace.");
        }
    }

    private static RunSummaryDto MapRun(RunRow row)
    {
        return new RunSummaryDto(row.RefId, row.WorkflowRefId, row.WorkflowVersion, row.Status, row.CorrelationKey, row.StartedAt, row.CompletedAt);
    }

    private static BranchSummaryDto MapBranch(BranchRow row)
    {
        return new BranchSummaryDto(row.RefId, row.Status, row.NodeId, row.CreatedAt, row.UpdatedAt);
    }
}
