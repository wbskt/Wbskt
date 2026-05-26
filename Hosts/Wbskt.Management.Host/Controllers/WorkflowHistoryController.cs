using Microsoft.AspNetCore.Mvc;
using Wbskt.Management.Models.Workflow;
using Wbskt.Primitives.Exceptions;
using Wbskt.Workflow.Abstraction.Providers;

namespace Wbskt.Management.Host.Controllers;

[Route("api/runs/{runRefId:guid}/history")]
[ApiController]
public sealed class WorkflowHistoryController(IRunProvider runProvider, IHistoryEventProvider historyProvider) : ControllerBase
{
    [HttpGet]
    public async Task<HistoryListResponse> List(Guid runRefId, [FromQuery] long fromEventId = 0, [FromQuery] int top = 200, CancellationToken ct = default)
    {
        int? runId = await runProvider.FindByRefIdAsync(runRefId, ct);
        if (!runId.HasValue)
        {
            throw new NotFoundException($"Run '{runRefId}' was not found.");
        }

        IReadOnlyCollection<Wbskt.Workflow.Abstraction.Entities.HistoryEventRow> rows = await historyProvider.GetByRunIdAsync(runId.Value, fromEventId, top + 1, ct);
        bool hasMore = rows.Count > top;
        IReadOnlyList<Wbskt.Workflow.Abstraction.Entities.HistoryEventRow> page = rows.Take(top).ToList();
        long? nextCursor = hasMore ? page.Last().HistoryEventId : null;
        return new HistoryListResponse(page.Select(row => new HistoryEventDto(row.HistoryEventId, row.Timestamp, row.EventKind, row.Severity, row.BranchRefId, row.NodeId, row.PayloadJson)).ToList(), nextCursor);
    }
}
