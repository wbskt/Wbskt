using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wbskt.Infrastructure;
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
public sealed class WorkflowHistoryController : ApiControllerBase
{
    private readonly IWorkflowRunQueryService _runQueryService;
    private readonly IHistoryEventProvider _historyProvider;
    private readonly IAuthServiceClient _authClient;
    private readonly ILogger<WorkflowHistoryController> _logger;

    public WorkflowHistoryController(
        IWorkflowRunQueryService runQueryService,
        IHistoryEventProvider historyProvider,
        IAuthServiceClient authClient,
        ILogger<WorkflowHistoryController> logger)
    {
        _runQueryService = runQueryService;
        _historyProvider = historyProvider;
        _authClient = authClient;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<HistoryListResponse>> List(Guid workspaceRef, Guid runRefId, [FromQuery] long fromEventId = 0, [FromQuery] int top = 200, CancellationToken ct = default)
    {
        _logger.LogInformation("API: List history requested for WorkspaceRef: '{WorkspaceRef}', RunRefId: '{RunRefId}'", workspaceRef, runRefId);

        var workspaceIdResult = await _authClient.ResolveWorkspaceAsync(workspaceRef, Permissions.WorkflowsRead, ct);
        if (workspaceIdResult.IsFailure)
        {
            return MapResult(Result<HistoryListResponse>.Failure(workspaceIdResult.Error));
        }

        var ensureRunResult = await _runQueryService.EnsureRunInWorkspaceAsync(workspaceIdResult.Value, runRefId, ct);
        if (ensureRunResult.IsFailure)
        {
            return MapResult(Result<HistoryListResponse>.Failure(ensureRunResult.Error));
        }

        try
        {
            IReadOnlyCollection<HistoryEventRow> rows = await _historyProvider.GetByRunIdAsync(ensureRunResult.Value, fromEventId, top + 1, ct);
            bool hasMore = rows.Count > top;
            IReadOnlyList<HistoryEventRow> page = rows.Take(top).ToList();
            long? nextCursor = hasMore ? page.Last().HistoryEventId : null;
            
            var resultDto = new HistoryListResponse(
                page.Select(row => new HistoryEventDto(
                    row.HistoryEventId, 
                    row.Timestamp, 
                    row.EventKind, 
                    row.Severity, 
                    row.BranchRefId, 
                    row.NodeId, 
                    row.PayloadJson
                )).ToList(), 
                nextCursor
            );

            return Ok(resultDto);
        }
        catch (Exception ex)
        {
            _logger.LogError("Unexpected error fetching run history for RunRefId: '{RunRefId}'. Error: {Message}", runRefId, ex.Message);
            _logger.LogTrace(ex, "List exception stack trace for RunRefId '{RunRefId}'", runRefId);
            return MapError(Error.Failure("RUN_HISTORY_ERROR", ex.Message));
        }
    }



}
