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
    private readonly IRunCancellationService _cancellationService;

    public WorkflowRunQueryService(IRunProvider runProvider, IBranchProvider branchProvider, IRunCancellationService cancellationService)
    {
        _runProvider = runProvider;
        _branchProvider = branchProvider;
        _cancellationService = cancellationService;
    }

    public async Task<RunListResponse> ListByWorkflowAsync(Guid workflowRefId, string? statusFilter, int top, long? cursorId, CancellationToken ct)
    {
        IReadOnlyCollection<RunRow> rows = await _runProvider.ListByWorkflowAsync(workflowRefId, statusFilter, top, cursorId, ct);
        IReadOnlyList<RunSummaryDto> runs = rows.Select(MapRun).ToList();
        long? nextCursor = runs.Count == top ? rows.Last().Id : null;
        return new RunListResponse(runs, nextCursor);
    }

    public async Task<RunDetailDto> GetDetailAsync(Guid runRefId, CancellationToken ct)
    {
        RunRow run = await _runProvider.GetByRefIdAsync(runRefId, ct);
        IReadOnlyCollection<BranchRow> branches = await _branchProvider.GetAllByRunIdAsync(run.Id, ct);
        return new RunDetailDto(MapRun(run), branches.Select(MapBranch).ToList());
    }

    public async Task CancelAsync(Guid runRefId, string reason, CancellationToken ct)
    {
        int? runId = await _runProvider.FindByRefIdAsync(runRefId, ct);
        if (!runId.HasValue)
        {
            throw new NotFoundException($"Run '{runRefId}' was not found.");
        }

        await _cancellationService.RequestCancellationAsync(runId.Value, reason, ct);
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
