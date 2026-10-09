using Wbskt.Infrastructure;
using Wbskt.Management.Models.Workflow;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;

namespace Wbskt.Management.Host.Services.Workflow;

public sealed class WorkflowRunQueryService : IWorkflowRunQueryService
{
    private const int TopFailures = 10;
    private const int TopSlowNodes = 20;

    /// <summary>Enough rows for a dashboard to show the workspace's real shape without paging.</summary>
    private const int TopWorkflows = 50;

    /// <summary>
    /// The statuses the engine's cancellation acts on (<c>RunCancellationService</c>). A 'Failing' run
    /// is still going on its other branches; a 'Cancelling' one is accepted again, as the engine repeats
    /// its cleanup. Every other status is terminal.
    /// </summary>
    private static readonly HashSet<string> CancellableStatuses = new(StringComparer.Ordinal) { "Running", "Failing", "Cancelling" };

    private readonly IRunProvider _runProvider;
    private readonly IBranchProvider _branchProvider;
    private readonly IWorkflowDefinitionProvider _workflowDefinitionProvider;
    private readonly ILogger<WorkflowRunQueryService> _logger;

    public WorkflowRunQueryService(
        IRunProvider runProvider,
        IBranchProvider branchProvider,
        IWorkflowDefinitionProvider workflowDefinitionProvider,
        ILogger<WorkflowRunQueryService> logger)
    {
        _runProvider = runProvider;
        _branchProvider = branchProvider;
        _workflowDefinitionProvider = workflowDefinitionProvider;
        _logger = logger;
    }

    public async Task<Result<RunListResponse>> ListByWorkflowAsync(int workspaceId, Guid workflowRefId, string? statusFilter, int top, long? cursorId, CancellationToken ct)
    {
        _logger.LogDebug("Querying run list for workflow RefId: '{WorkflowRefId}' in WorkspaceId: {WorkspaceId}", workflowRefId, workspaceId);

        var ensureWorkflowResult = await EnsureWorkflowInWorkspaceAsync(workspaceId, workflowRefId, ct);
        if (ensureWorkflowResult.IsFailure)
        {
            return Result<RunListResponse>.Failure(ensureWorkflowResult.Error);
        }

        // Fetch one more than asked for: its presence is what proves another page exists. Using
        // "a full page means there's more" hands back a cursor even when the page landed exactly
        // on the end, so clients always fetched one empty page.
        IReadOnlyCollection<RunRow> rows = await _runProvider.ListByWorkflowAsync(workflowRefId, statusFilter, top + 1, cursorId, ct);

        return Result<RunListResponse>.Success(BuildPage(rows, top));
    }

    public async Task<Result<RunListResponse>> ListByWorkspaceAsync(int workspaceId, string? statusFilter, int top, long? cursorId, CancellationToken ct)
    {
        _logger.LogDebug("Querying run list for WorkspaceId: {WorkspaceId}", workspaceId);

        // The procedure filters by workspace itself, so there is no per-workflow membership check
        // to do here - a run cannot appear unless its definition belongs to this workspace.
        IReadOnlyCollection<RunRow> rows = await _runProvider.ListByWorkspaceAsync(workspaceId, statusFilter, top + 1, cursorId, ct);

        return Result<RunListResponse>.Success(BuildPage(rows, top));
    }

    /// <summary>
    /// Trims the extra row fetched to probe for a next page, and derives the cursor from whether that
    /// row was actually there.
    /// </summary>
    private static RunListResponse BuildPage(IReadOnlyCollection<RunRow> rows, int top)
    {
        bool hasMore = rows.Count > top;
        IReadOnlyList<RunRow> page = rows.Take(top).ToList();
        long? nextCursor = hasMore && page.Count > 0 ? page[^1].Id : null;

        return new RunListResponse(page.Select(MapRun).ToList(), nextCursor);
    }

    public async Task<Result<WorkflowStatsResponse>> GetStatsAsync(int workspaceId, Guid workflowRefId, DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        _logger.LogDebug("Querying stats for workflow RefId: '{WorkflowRefId}'", workflowRefId);

        if (toUtc <= fromUtc)
        {
            return Result<WorkflowStatsResponse>.Failure(Error.Validation("INVALID_WINDOW", "'to' must be later than 'from'."));
        }

        var ensureWorkflowResult = await EnsureWorkflowInWorkspaceAsync(workspaceId, workflowRefId, ct);
        if (ensureWorkflowResult.IsFailure)
        {
            return Result<WorkflowStatsResponse>.Failure(ensureWorkflowResult.Error);
        }

        RunStatsRow stats = await _runProvider.GetStatsAsync(workflowRefId, fromUtc, toUtc, ct);
        IReadOnlyCollection<RunFailureBucketRow> failures = await _runProvider.GetTopFailuresAsync(workflowRefId, fromUtc, toUtc, TopFailures, ct);
        IReadOnlyCollection<NodeTimingRow> timings = await _runProvider.GetNodeTimingsAsync(workflowRefId, fromUtc, toUtc, TopSlowNodes, ct);

        // Success rate is over FINISHED runs. Counting in-flight ones as failures would make an
        // active workflow look broken, and dividing by zero when nothing has finished would report
        // 0% - which reads as "everything failed" rather than "nothing to report".
        int finished = stats.TotalRuns - stats.ActiveCount;
        double? successRate = finished > 0 ? (double)stats.SucceededCount / finished : null;

        return Result<WorkflowStatsResponse>.Success(new WorkflowStatsResponse(
            workflowRefId,
            fromUtc,
            toUtc,
            new RunOutcomeCountsDto(
                stats.TotalRuns,
                stats.SucceededCount,
                stats.FailedCount,
                stats.PartiallyFailedCount,
                stats.CancelledCount,
                stats.FaultedCount,
                stats.OutOfCreditsCount,
                stats.ActiveCount),
            new RunDurationsDto(stats.P50DurationMs, stats.P95DurationMs, stats.MaxDurationMs, stats.AvgDurationMs),
            successRate,
            failures.Select(f => new FailureBucketDto(f.ErrorCode, f.NodeId, f.Occurrences, f.LastSeenAt)).ToList(),
            timings.Select(t => new NodeTimingDto(t.NodeId, t.Executions, t.FailureCount, t.AvgDurationMs, t.MaxDurationMs)).ToList()));
    }

    public async Task<Result<WorkspaceStatsResponse>> GetWorkspaceStatsAsync(int workspaceId, DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        _logger.LogDebug("Querying stats for WorkspaceId: {WorkspaceId}", workspaceId);

        if (toUtc <= fromUtc)
        {
            return Result<WorkspaceStatsResponse>.Failure(Error.Validation("INVALID_WINDOW", "'to' must be later than 'from'."));
        }

        // No per-workflow ownership check to repeat: both procedures scope by workspace themselves,
        // so a workflow from another workspace cannot appear in the result at all.
        RunStatsRow stats = await _runProvider.GetWorkspaceStatsAsync(workspaceId, fromUtc, toUtc, ct);
        IReadOnlyCollection<WorkflowRunSummaryRow> perWorkflow = await _runProvider.GetPerWorkflowStatsAsync(workspaceId, fromUtc, toUtc, TopWorkflows, ct);

        return Result<WorkspaceStatsResponse>.Success(new WorkspaceStatsResponse(
            fromUtc,
            toUtc,
            new RunOutcomeCountsDto(
                stats.TotalRuns,
                stats.SucceededCount,
                stats.FailedCount,
                stats.PartiallyFailedCount,
                stats.CancelledCount,
                stats.FaultedCount,
                stats.OutOfCreditsCount,
                stats.ActiveCount),
            new RunDurationsDto(stats.P50DurationMs, stats.P95DurationMs, stats.MaxDurationMs, stats.AvgDurationMs),
            SuccessRate(stats.TotalRuns, stats.ActiveCount, stats.SucceededCount),
            perWorkflow.Select(w => new WorkflowRunSummaryDto(
                w.WorkflowRefId,
                w.TotalRuns,
                w.SucceededCount,
                w.FailedCount,
                w.ActiveCount,
                w.AvgDurationMs,
                SuccessRate(w.TotalRuns, w.ActiveCount, w.SucceededCount))).ToList()));
    }

    /// <summary>
    /// Succeeded ÷ finished. In-flight runs are excluded from the denominator, or an active workflow
    /// reads as broken; null rather than 0 when nothing has finished, because 0 reads as "everything
    /// failed" rather than "nothing to report".
    /// </summary>
    private static double? SuccessRate(int total, int active, int succeeded)
    {
        int finished = total - active;
        return finished > 0 ? (double)succeeded / finished : null;
    }

    public async Task<Result<RunDetailDto>> GetDetailAsync(int workspaceId, Guid runRefId, CancellationToken ct)
    {
        _logger.LogDebug("Querying run details for RunRefId: '{RunRefId}' in WorkspaceId: {WorkspaceId}", runRefId, workspaceId);

        var runResult = await LoadRunAsync(workspaceId, runRefId, ct);
        if (runResult.IsFailure)
        {
            return Result<RunDetailDto>.Failure(runResult.Error);
        }

        RunRow run = runResult.Value;
        IReadOnlyCollection<BranchRow> branches = await _branchProvider.GetAllByRunIdAsync(run.Id, ct);
        return Result<RunDetailDto>.Success(new RunDetailDto(MapRun(run), branches.Select(MapBranch).ToList()));
    }

    public async Task<Result<int>> ResolveCancellableRunAsync(int workspaceId, Guid runRefId, CancellationToken ct)
    {
        _logger.LogDebug("Checking run '{RunRefId}' in WorkspaceId: {WorkspaceId} can be cancelled", runRefId, workspaceId);

        // Another workspace's run reads exactly like an unknown one, so a cancel cannot be used to
        // learn that a run exists elsewhere.
        var runResult = await LoadRunAsync(workspaceId, runRefId, ct);
        if (runResult.IsFailure)
        {
            return Result<int>.Failure(runResult.Error);
        }

        RunRow run = runResult.Value;

        // The cancel itself happens later, on the engine, so this read is what keeps a cancel of a
        // finished run answering 409 rather than an accepted request that does nothing. A run that
        // finishes after this read is left alone by the engine.
        if (!CancellableStatuses.Contains(run.Status))
        {
            _logger.LogInformation("Run '{RunRefId}' is {Status}; nothing to cancel.", runRefId, run.Status);
            return Result<int>.Failure(Error.Conflict("RUN_NOT_CANCELLABLE", "The run has already reached a terminal status."));
        }

        return Result<int>.Success(run.Id);
    }

    public async Task<Result<int>> EnsureRunInWorkspaceAsync(int workspaceId, Guid runRefId, CancellationToken ct)
    {
        _logger.LogDebug("Verifying run '{RunRefId}' belongs to WorkspaceId: {WorkspaceId}", runRefId, workspaceId);

        var runResult = await LoadRunAsync(workspaceId, runRefId, ct);
        return runResult.IsFailure
            ? Result<int>.Failure(runResult.Error)
            : Result<int>.Success(runResult.Value.Id);
    }

    /// <summary>
    /// The run, when <paramref name="workspaceId"/> owns it. A run carries no workspace of its own; it
    /// belongs to the workspace of the definition version it ran, so a run whose definition is missing
    /// or elsewhere reads as not found.
    /// </summary>
    private async Task<Result<RunRow>> LoadRunAsync(int workspaceId, Guid runRefId, CancellationToken ct)
    {
        RunRow? run = await _runProvider.FindRowByRefIdAsync(runRefId, ct);
        if (run is null)
        {
            return Result<RunRow>.Failure(WorkspaceOwnership.RunNotFound);
        }

        WorkflowDefinitionRow? definition = await _workflowDefinitionProvider.FindRowByRefIdVersionAsync(run.WorkflowRefId, run.WorkflowVersion, ct);
        if (definition is null || definition.WorkspaceId != workspaceId)
        {
            _logger.LogWarning("Run '{RunRefId}' is not in WorkspaceId: {WorkspaceId}", runRefId, workspaceId);
            return Result<RunRow>.Failure(WorkspaceOwnership.RunNotFound);
        }

        return Result<RunRow>.Success(run);
    }

    private async Task<Result> EnsureWorkflowInWorkspaceAsync(int workspaceId, Guid workflowRefId, CancellationToken ct)
    {
        WorkflowDefinitionRow? definition = await _workflowDefinitionProvider.FindCurrentByRefIdAsync(workflowRefId, ct);
        if (definition is null || definition.WorkspaceId != workspaceId)
        {
            _logger.LogWarning("Workflow '{WorkflowRefId}' is not in WorkspaceId: {WorkspaceId}", workflowRefId, workspaceId);
            return Result.Failure(WorkspaceOwnership.WorkflowNotFound);
        }
        return Result.Success();
    }

    private static RunSummaryDto MapRun(RunRow row)
    {
        return new RunSummaryDto(row.RefId, row.WorkflowRefId, row.WorkflowVersion, row.Status, row.CorrelationKey, row.StartedAt, row.CompletedAt);
    }

    private static BranchSummaryDto MapBranch(BranchRow row)
    {
        return new BranchSummaryDto(row.RefId, row.Status, row.NodeId, row.CreatedAt, row.UpdatedAt, row.LocalJson);
    }
}
