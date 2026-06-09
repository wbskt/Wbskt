using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wbskt.Management.Host.Services.Clients;
using Wbskt.Management.Host.Services.Workflow;
using Wbskt.Management.Models.Workflow;
using Wbskt.Primitives.Constants;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;

namespace Wbskt.Management.Host.Controllers.Workflow;

[Route("api/workspaces/{workspaceRef:guid}/runs/{runRefId:guid}/history")]
[ApiController]
[Authorize]
public sealed class WorkflowHistoryController(
    IWorkflowRunQueryService runQueryService,
    IHistoryEventProvider historyProvider,
    IAuthServiceClient authClient) : ControllerBase
{
    [HttpGet]
    public async Task<HistoryListResponse> List(Guid workspaceRef, Guid runRefId, [FromQuery] long fromEventId = 0, [FromQuery] int top = 200, CancellationToken ct = default)
    {
        int workspaceId = await authClient.ResolveWorkspaceAsync(workspaceRef, Permissions.WorkflowsRead, ct);
        int runId = await runQueryService.EnsureRunInWorkspaceAsync(workspaceId, runRefId, ct);

        IReadOnlyCollection<HistoryEventRow> rows = await historyProvider.GetByRunIdAsync(runId, fromEventId, top + 1, ct);
        bool hasMore = rows.Count > top;
        IReadOnlyList<HistoryEventRow> page = rows.Take(top).ToList();
        long? nextCursor = hasMore ? page.Last().HistoryEventId : null;
        return new HistoryListResponse(page.Select(row => new HistoryEventDto(row.HistoryEventId, row.Timestamp, row.EventKind, row.Severity, row.BranchRefId, row.NodeId, row.PayloadJson)).ToList(), nextCursor);
    }
}
