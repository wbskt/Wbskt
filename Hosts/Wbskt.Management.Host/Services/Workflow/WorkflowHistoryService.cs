using Wbskt.Infrastructure;
using Wbskt.Management.Models.Workflow;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;

namespace Wbskt.Management.Host.Services.Workflow;

public sealed class WorkflowHistoryService : IWorkflowHistoryService
{
    private readonly IWorkflowRunQueryService _runs;
    private readonly IHistoryEventProvider _history;

    public WorkflowHistoryService(IWorkflowRunQueryService runs, IHistoryEventProvider history)
    {
        _runs = runs;
        _history = history;
    }

    public async Task<Result<HistoryListResponse>> ListAsync(int workspaceId, Guid runRefId, long fromEventId, int top, CancellationToken ct)
    {
        var ensureRunResult = await _runs.EnsureRunInWorkspaceAsync(workspaceId, runRefId, ct);
        if (ensureRunResult.IsFailure)
        {
            return Result<HistoryListResponse>.Failure(ensureRunResult.Error);
        }

        // One row past the page proves another page exists.
        IReadOnlyCollection<HistoryEventRow> rows = await _history.GetByRunIdAsync(ensureRunResult.Value, fromEventId, top + 1, ct);
        bool hasMore = rows.Count > top;
        IReadOnlyList<HistoryEventRow> page = rows.Take(top).ToList();
        long? nextCursor = hasMore ? page.Last().HistoryEventId : null;

        return Result<HistoryListResponse>.Success(new HistoryListResponse(
            page.Select(row => new HistoryEventDto(
                row.HistoryEventId,
                row.Timestamp,
                row.EventKind,
                row.Severity,
                row.BranchRefId,
                row.NodeId,
                row.PayloadJson)).ToList(),
            nextCursor));
    }
}
