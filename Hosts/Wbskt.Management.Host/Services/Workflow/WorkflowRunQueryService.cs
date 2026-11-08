using Wbskt.Models;
using System.Text.Json;
using Wbskt.Infrastructure;
using Wbskt.Management.Models.Workflow;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Management.Host.Services.Workflow;

public sealed class WorkflowRunQueryService : IWorkflowRunQueryService
{
    private const int TopFailures = 10;
    private const int TopSlowNodes = 20;

    /// <summary>Enough rows for a dashboard to show the workspace's real shape without paging.</summary>
    private const int TopWorkflows = 50;

    /// <summary>The window stats cover when the caller names no 'from'.</summary>
    private static readonly TimeSpan DefaultStatsRange = TimeSpan.FromDays(30);

    /// <summary>The longest window stats cover: the same cap as a client's readings.</summary>
    private static readonly TimeSpan MaxStatsRange = TimeSpan.FromDays(400);

    /// <summary>
    /// The longest window whose workspace rollup also gives each workflow its own days: a card strip is
    /// 30 days, and 400 days across 50 workflows would be 20,000 entries in one response.
    /// </summary>
    private static readonly TimeSpan MaxPerWorkflowDaysRange = TimeSpan.FromDays(92);

    /// <summary>
    /// The statuses the engine's cancellation acts on (<c>RunCancellationService</c>). A 'Failing' run
    /// is still going on its other branches; a 'Cancelling' one is accepted again, as the engine repeats
    /// its cleanup. Every other status is terminal.
    /// </summary>
    private static readonly HashSet<string> CancellableStatuses = new(StringComparer.Ordinal) { RunStatus.Running, RunStatus.Failing, RunStatus.Cancelling };

    private static readonly JsonSerializerOptions DefinitionJson = new(JsonSerializerDefaults.Web);

    private readonly IRunProvider _runProvider;
    private readonly IBranchProvider _branchProvider;
    private readonly IWorkflowDefinitionProvider _workflowDefinitionProvider;
    private readonly ILogger<WorkflowRunQueryService> _logger;
    private readonly IWorkflowDraftProvider? _draftProvider;

    public WorkflowRunQueryService(
        IRunProvider runProvider,
        IBranchProvider branchProvider,
        IWorkflowDefinitionProvider workflowDefinitionProvider,
        ILogger<WorkflowRunQueryService> logger,
        IWorkflowDraftProvider? draftProvider = null)
    {
        _draftProvider = draftProvider;
        _runProvider = runProvider;
        _branchProvider = branchProvider;
        _workflowDefinitionProvider = workflowDefinitionProvider;
        _logger = logger;
    }

    public async Task<Result<RunListResponse>> ListByWorkflowAsync(int workspaceId, Guid workflowRefId, string? statusFilter, bool? isTest, int top, long? cursorId, CancellationToken ct)
    {
        _logger.LogDebug("Querying run list for workflow RefId: '{WorkflowRefId}' in WorkspaceId: {WorkspaceId}", workflowRefId, workspaceId);

        var ensureWorkflowResult = await WorkflowOwnershipCheck.EnsureCurrentAsync(_workflowDefinitionProvider, _logger, workspaceId, workflowRefId, ct);
        // A workflow never published has no version, only a draft, but its test runs are listed.
        if (ensureWorkflowResult.IsFailure
            && (_draftProvider is null || await _draftProvider.FindAsync(workflowRefId, workspaceId, ct) is null))
        {
            return Result<RunListResponse>.Failure(ensureWorkflowResult.Error);
        }

        // Fetch one more than asked for: its presence is what proves another page exists. Using
        // "a full page means there's more" hands back a cursor even when the page landed exactly
        // on the end, so clients always fetched one empty page.
        IReadOnlyCollection<RunRow> rows = await _runProvider.ListByWorkflowAsync(workflowRefId, statusFilter, isTest, top + 1, cursorId, ct);

        return Result<RunListResponse>.Success(await BuildPageAsync(rows, top, ct));
    }

    public async Task<Result<RunListResponse>> ListByWorkspaceAsync(int workspaceId, string? statusFilter, bool? isTest, int top, long? cursorId, CancellationToken ct)
    {
        _logger.LogDebug("Querying run list for WorkspaceId: {WorkspaceId}", workspaceId);

        // The procedure filters by workspace itself, so there is no per-workflow membership check
        // to do here - a run cannot appear unless its definition belongs to this workspace.
        IReadOnlyCollection<RunRow> rows = await _runProvider.ListByWorkspaceAsync(workspaceId, statusFilter, isTest, top + 1, cursorId, ct);

        return Result<RunListResponse>.Success(await BuildPageAsync(rows, top, ct));
    }

    private static Result<TimeRange> ResolveStatsRange(DateTimeOffset? from, DateTimeOffset? to)
        => TimeRange.Resolve(from, to, TimeProvider.System.GetUtcNow(), DefaultStatsRange, MaxStatsRange);

    /// <summary>
    /// Trims the extra row fetched to probe for a next page, and derives the cursor from whether that
    /// row was actually there.
    /// </summary>
    private async Task<RunListResponse> BuildPageAsync(IReadOnlyCollection<RunRow> rows, int top, CancellationToken ct)
    {
        bool hasMore = rows.Count > top;
        IReadOnlyList<RunRow> page = rows.Take(top).ToList();
        long? nextKey = hasMore && page.Count > 0 ? page[^1].Id : null;

        var details = (await _runProvider.GetListDetailsAsync([.. page.Select(r => r.Id)], ct) ?? []).ToDictionary(d => d.RunId);
        var versions = await LoadVersionsAsync(page.Select(r => r.WorkflowDefinitionId).Distinct(), ct);

        var items = page.Select(run => MapListedRun(run, details.GetValueOrDefault(run.Id), versions.GetValueOrDefault(run.WorkflowDefinitionId))).ToList();
        return new RunListResponse { Items = items, NextCursor = PageRequest.KeyCursor(nextKey) };
    }

    // Definition rows never change once published, so a page reads each version it spans once.
    private async Task<Dictionary<int, ListedVersion>> LoadVersionsAsync(IEnumerable<int> definitionIds, CancellationToken ct)
    {
        var versions = new Dictionary<int, ListedVersion>();
        foreach (int id in definitionIds)
        {
            WorkflowDefinitionRow? row = await _workflowDefinitionProvider.GetByIdAsync(id, ct);
            if (row is null)
            {
                continue;
            }

            IReadOnlyDictionary<Guid, BaseNode> nodes;
            try
            {
                WorkflowDefinition? definition = JsonSerializer.Deserialize<WorkflowDefinition>(row.DefinitionJson, DefinitionJson);
                nodes = definition?.Nodes.GroupBy(n => n.NodeId).ToDictionary(g => g.Key, g => g.First()) ?? [];
            }
            catch (JsonException ex)
            {
                // A run list must still load when one old version no longer parses; its rows just lose their step names.
                _logger.LogWarning(ex, "Could not read workflow definition {DefinitionId} for the run list.", id);
                nodes = new Dictionary<Guid, BaseNode>();
            }

            versions[id] = new ListedVersion(row.Name, nodes);
        }

        return versions;
    }

    private static RunSummaryDto MapListedRun(RunRow row, RunListDetailRow? detail, ListedVersion? version)
    {
        BaseNode? Node(Guid id) => version?.Nodes.GetValueOrDefault(id);
        RunStepDto? Step(Guid? id, string? errorCode = null, int? attempt = null, int? maxAttempts = null) =>
            id is { } nodeId ? new RunStepDto(nodeId, Node(nodeId)?.Name, Node(nodeId)?.Kind, errorCode, attempt, maxAttempts) : null;

        BaseNode? trigger = Node(row.TriggerNodeId);
        return MapRun(row) with
        {
            Trigger = new RunTriggerDto(row.TriggerNodeId, trigger?.Kind, trigger?.Name, RunTriggerSummaries.For(trigger, row.ParentRunRefId)),
            CreditsUsed = detail?.CreditsConsumed,
            FailedAt = Step(detail?.FailedNodeId, detail?.FailedErrorCode),
            EndedAt = Step(detail?.EndedNodeId),
            CurrentStep = Step(detail?.CurrentNodeId, attempt: detail?.CurrentAttempt, maxAttempts: detail?.CurrentMaxAttempts),
            WorkflowName = version?.Name,
            Waiting = MapWaiting(detail)
        };
    }

    private static RunWaitingDto? MapWaiting(RunListDetailRow? detail)
    {
        return detail is { IsWaiting: true }
            ? new RunWaitingDto(detail.WaitingKind, detail.WaitingNodeId, detail.WaitingExpiresAt.ToUtcOffset(), detail.WaitingSignalName)
            : null;
    }

    private sealed record ListedVersion(string Name, IReadOnlyDictionary<Guid, BaseNode> Nodes);

    public async Task<Result<WorkflowStatsResponse>> GetStatsAsync(int workspaceId, Guid workflowRefId, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct)
    {
        _logger.LogDebug("Querying stats for workflow RefId: '{WorkflowRefId}'", workflowRefId);

        var range = ResolveStatsRange(from, to);
        if (range.IsFailure)
        {
            return Result<WorkflowStatsResponse>.Failure(range.Error);
        }

        var (fromUtc, toUtc) = range.Value;

        var ensureWorkflowResult = await WorkflowOwnershipCheck.EnsureCurrentAsync(_workflowDefinitionProvider, _logger, workspaceId, workflowRefId, ct);
        if (ensureWorkflowResult.IsFailure)
        {
            return Result<WorkflowStatsResponse>.Failure(ensureWorkflowResult.Error);
        }

        RunStatsRow stats = await _runProvider.GetStatsAsync(workflowRefId, fromUtc, toUtc, ct);
        IReadOnlyCollection<RunFailureBucketRow> failures = await _runProvider.GetTopFailuresAsync(workflowRefId, fromUtc, toUtc, TopFailures, ct);
        IReadOnlyCollection<NodeTimingRow> timings = await _runProvider.GetNodeTimingsAsync(workflowRefId, fromUtc, toUtc, TopSlowNodes, ct);
        IReadOnlyCollection<RunDailyStatsRow> daily = await _runProvider.GetDailyStatsAsync(workflowRefId, fromUtc, toUtc, ct) ?? [];

        // Success rate is over FINISHED runs. Counting in-flight ones as failures would make an
        // active workflow look broken, and dividing by zero when nothing has finished would report
        // 0% - which reads as "everything failed" rather than "nothing to report".
        int finished = stats.TotalRuns - stats.ActiveCount;
        double? successRate = finished > 0 ? (double)stats.SucceededCount / finished : null;

        return Result<WorkflowStatsResponse>.Success(new WorkflowStatsResponse(
            workflowRefId,
            fromUtc.ToUtcOffset(),
            toUtc.ToUtcOffset(),
            new RunOutcomeCountsDto(
                stats.TotalRuns,
                stats.SucceededCount,
                stats.FailedCount,
                stats.PartiallyFailedCount,
                stats.CancelledCount,
                Faulted: 0,
                stats.OutOfCreditsCount,
                stats.ActiveCount,
                Running: stats.ActiveCount - stats.WaitingCount,
                Waiting: stats.WaitingCount),
            new RunDurationsDto(stats.P50DurationMs, stats.P95DurationMs, stats.MaxDurationMs, stats.AvgDurationMs),
            successRate,
            failures.Select(f => new FailureBucketDto(f.ErrorCode, f.NodeId, f.Occurrences, f.LastSeenAt.ToUtcOffset())).ToList(),
            timings.Select(t => new NodeTimingDto(t.NodeId, t.Executions, t.FailureCount, t.AvgDurationMs, t.MaxDurationMs)).ToList(),
            FillDays(fromUtc, toUtc, daily)));
    }

    public async Task<Result<WorkspaceStatsResponse>> GetWorkspaceStatsAsync(int workspaceId, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct)
    {
        _logger.LogDebug("Querying stats for WorkspaceId: {WorkspaceId}", workspaceId);

        var range = ResolveStatsRange(from, to);
        if (range.IsFailure)
        {
            return Result<WorkspaceStatsResponse>.Failure(range.Error);
        }

        var (fromUtc, toUtc) = range.Value;

        // No per-workflow ownership check to repeat: both procedures scope by workspace themselves,
        // so a workflow from another workspace cannot appear in the result at all.
        RunStatsRow stats = await _runProvider.GetWorkspaceStatsAsync(workspaceId, fromUtc, toUtc, ct);
        IReadOnlyCollection<WorkflowRunSummaryRow> perWorkflow = await _runProvider.GetPerWorkflowStatsAsync(workspaceId, fromUtc, toUtc, TopWorkflows, ct);
        IReadOnlyCollection<RunDailyStatsRow> daily = await _runProvider.GetWorkspaceDailyStatsAsync(workspaceId, fromUtc, toUtc, ct) ?? [];
        ILookup<Guid, RunDailyStatsRow> dailyByWorkflow = daily.ToLookup(d => d.WorkflowRefId);
        bool perWorkflowDays = toUtc - fromUtc <= MaxPerWorkflowDaysRange;

        return Result<WorkspaceStatsResponse>.Success(new WorkspaceStatsResponse(
            fromUtc.ToUtcOffset(),
            toUtc.ToUtcOffset(),
            new RunOutcomeCountsDto(
                stats.TotalRuns,
                stats.SucceededCount,
                stats.FailedCount,
                stats.PartiallyFailedCount,
                stats.CancelledCount,
                Faulted: 0,
                stats.OutOfCreditsCount,
                stats.ActiveCount,
                Running: stats.ActiveCount - stats.WaitingCount,
                Waiting: stats.WaitingCount),
            new RunDurationsDto(stats.P50DurationMs, stats.P95DurationMs, stats.MaxDurationMs, stats.AvgDurationMs),
            SuccessRate(stats.TotalRuns, stats.ActiveCount, stats.SucceededCount),
            perWorkflow.Select(w => new WorkflowRunSummaryDto(
                w.WorkflowRefId,
                w.TotalRuns,
                w.SucceededCount,
                w.FailedCount,
                w.ActiveCount,
                w.AvgDurationMs,
                SuccessRate(w.TotalRuns, w.ActiveCount, w.SucceededCount),
                Running: w.ActiveCount - w.WaitingCount,
                Waiting: w.WaitingCount,
                perWorkflowDays ? FillDays(fromUtc, toUtc, dailyByWorkflow[w.WorkflowRefId]) : null)).ToList(),
            FillDays(fromUtc, toUtc, daily)));
    }

    /// <summary>
    /// Every UTC day the window touches, oldest first, with zeros on days without runs so a chart need
    /// not fill gaps. Rows for the same day (several workflows) are summed; a median cannot be summed,
    /// so a summed day has none.
    /// </summary>
    private static List<RunDayDto> FillDays(DateTime fromUtc, DateTime toUtc, IEnumerable<RunDailyStatsRow> rows)
    {
        Dictionary<DateOnly, RunDayDto> byDay = rows
            .GroupBy(r => r.Day)
            .ToDictionary(g => g.Key, g => new RunDayDto(
                g.Key,
                g.Sum(r => r.TotalRuns),
                g.Sum(r => r.SucceededCount),
                g.Sum(r => r.FailedCount),
                g.Sum(r => r.CancelledCount),
                g.Count() == 1 ? g.First().P50DurationMs : null));

        var days = new List<RunDayDto>();
        DateOnly last = DateOnly.FromDateTime(toUtc.AddTicks(-1));
        for (DateOnly day = DateOnly.FromDateTime(fromUtc); day <= last; day = day.AddDays(1))
        {
            days.Add(byDay.GetValueOrDefault(day) ?? new RunDayDto(day, 0, 0, 0, 0));
        }

        return days;
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

        // Only a Running run can be waiting, so a finished run's page skips the extra read.
        RunListDetailRow? detail = run.Status == RunStatus.Running
            ? (await _runProvider.GetListDetailsAsync([run.Id], ct) ?? []).FirstOrDefault()
            : null;

        RunSummaryDto summary = MapRun(run) with { Waiting = MapWaiting(detail) };
        return Result<RunDetailDto>.Success(new RunDetailDto(summary, branches.Select(MapBranch).ToList()));
    }

    public async Task<Result<WorkflowDefinitionDto>> GetDefinitionAsync(int workspaceId, Guid runRefId, CancellationToken ct)
    {
        var loaded = await LoadRunAndDefinitionAsync(workspaceId, runRefId, ct);
        if (loaded.IsFailure)
        {
            return Result<WorkflowDefinitionDto>.Failure(loaded.Error);
        }

        WorkflowDefinitionRow row = loaded.Value.Definition;
        WorkflowDefinition definition = JsonSerializer.Deserialize<WorkflowDefinition>(row.DefinitionJson, DefinitionJson)
            ?? throw new InvalidOperationException($"Could not deserialize the definition run '{runRefId}' ran.");

        // A test copy is no published version, whatever its IsEnabled says.
        string status = row.IsTestCopy ? "Test" : row.IsEnabled ? "Published" : "Deprecated";

        return Result<WorkflowDefinitionDto>.Success(new WorkflowDefinitionDto(
            row.RefId,
            row.Version,
            status,
            row.Name,
            row.Description,
            definition with { WorkspaceId = row.WorkspaceId, PublishedBy = row.PublishedBy },
            row.CreatedAt.ToUtcOffset()));
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
    /// belongs to the workspace of the definition it ran, read by Id rather than by version so a test
    /// run (whose copy is no version) resolves too.
    /// </summary>
    private async Task<Result<RunRow>> LoadRunAsync(int workspaceId, Guid runRefId, CancellationToken ct)
    {
        var loaded = await LoadRunAndDefinitionAsync(workspaceId, runRefId, ct);
        return loaded.IsFailure ? Result<RunRow>.Failure(loaded.Error) : Result<RunRow>.Success(loaded.Value.Run);
    }

    private async Task<Result<(RunRow Run, WorkflowDefinitionRow Definition)>> LoadRunAndDefinitionAsync(int workspaceId, Guid runRefId, CancellationToken ct)
    {
        RunRow? run = await _runProvider.FindRowByRefIdAsync(runRefId, ct);
        if (run is null)
        {
            return Result<(RunRow, WorkflowDefinitionRow)>.Failure(WorkspaceOwnership.RunNotFound);
        }

        WorkflowDefinitionRow definition = await _workflowDefinitionProvider.GetByIdAsync(run.WorkflowDefinitionId, ct);
        if (definition.WorkspaceId != workspaceId)
        {
            _logger.LogWarning("Run '{RunRefId}' is not in WorkspaceId: {WorkspaceId}", runRefId, workspaceId);
            return Result<(RunRow, WorkflowDefinitionRow)>.Failure(WorkspaceOwnership.RunNotFound);
        }

        return Result<(RunRow, WorkflowDefinitionRow)>.Success((run, definition));
    }

    private static RunSummaryDto MapRun(RunRow row)
    {
        return new RunSummaryDto(row.RefId, row.WorkflowRefId, row.WorkflowVersion, row.Status, row.CorrelationKey, row.StartedAt.ToUtcOffset(), row.CompletedAt.ToUtcOffset(), Number: row.Number, IsTest: row.IsTest);
    }

    private static BranchSummaryDto MapBranch(BranchRow row)
    {
        return new BranchSummaryDto(row.RefId, row.Status, row.NodeId, row.CreatedAt.ToUtcOffset(), row.UpdatedAt.ToUtcOffset(), row.LocalJson);
    }
}
