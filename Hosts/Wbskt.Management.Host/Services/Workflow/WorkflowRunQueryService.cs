using Wbskt.Infrastructure;
using Wbskt.Management.Models.Workflow;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Management.Host.Services.Workflow;

public sealed class WorkflowRunQueryService : IWorkflowRunQueryService
{
    private readonly IRunProvider _runProvider;
    private readonly IBranchProvider _branchProvider;
    private readonly IWorkflowDefinitionProvider _workflowDefinitionProvider;
    private readonly IRunCancellationService _cancellationService;
    private readonly ILogger<WorkflowRunQueryService> _logger;

    public WorkflowRunQueryService(
        IRunProvider runProvider,
        IBranchProvider branchProvider,
        IWorkflowDefinitionProvider workflowDefinitionProvider,
        IRunCancellationService cancellationService,
        ILogger<WorkflowRunQueryService> logger)
    {
        _runProvider = runProvider;
        _branchProvider = branchProvider;
        _workflowDefinitionProvider = workflowDefinitionProvider;
        _cancellationService = cancellationService;
        _logger = logger;
    }

    public async Task<Result<RunListResponse>> ListByWorkflowAsync(int workspaceId, Guid workflowRefId, string? statusFilter, int top, long? cursorId, CancellationToken ct)
    {
        _logger.LogDebug("Querying run list for workflow RefId: '{WorkflowRefId}' in WorkspaceId: {WorkspaceId}", workflowRefId, workspaceId);

        try
        {
            var ensureWorkflowResult = await EnsureWorkflowInWorkspaceAsync(workspaceId, workflowRefId, ct);
            if (ensureWorkflowResult.IsFailure)
            {
                return Result<RunListResponse>.Failure(ensureWorkflowResult.Error);
            }

            IReadOnlyCollection<RunRow> rows = await _runProvider.ListByWorkflowAsync(workflowRefId, statusFilter, top, cursorId, ct);
            IReadOnlyList<RunSummaryDto> runs = rows.Select(MapRun).ToList();
            long? nextCursor = runs.Count == top ? rows.Last().Id : null;
            
            return Result<RunListResponse>.Success(new RunListResponse(runs, nextCursor));
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to query run list for workflow RefId: '{WorkflowRefId}'. Error: {Message}", workflowRefId, ex.Message);
            _logger.LogTrace(ex, "ListByWorkflowAsync exception stack trace for '{WorkflowRefId}'", workflowRefId);
            return Result<RunListResponse>.Failure(Error.Failure("RUN_QUERY_ERROR", ex.Message));
        }
    }

    public async Task<Result<RunDetailDto>> GetDetailAsync(int workspaceId, Guid runRefId, CancellationToken ct)
    {
        _logger.LogDebug("Querying run details for RunRefId: '{RunRefId}' in WorkspaceId: {WorkspaceId}", runRefId, workspaceId);

        try
        {
            RunRow run;
            try
            {
                run = await _runProvider.GetByRefIdAsync(runRefId, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Run details query failed: RunRefId '{RunRefId}' not found. Error: {Message}", runRefId, ex.Message);
                return Result<RunDetailDto>.Failure(Error.NotFound("RUN_NOT_FOUND", "Run not found."));
            }

            var ensureWorkspaceResult = await EnsureRunRowInWorkspaceAsync(workspaceId, run, ct);
            if (ensureWorkspaceResult.IsFailure)
            {
                return Result<RunDetailDto>.Failure(ensureWorkspaceResult.Error);
            }

            IReadOnlyCollection<BranchRow> branches = await _branchProvider.GetAllByRunIdAsync(run.Id, ct);
            return Result<RunDetailDto>.Success(new RunDetailDto(MapRun(run), branches.Select(MapBranch).ToList()));
        }
        catch (Exception ex)
        {
            _logger.LogError("Unexpected error querying run details for RunRefId: '{RunRefId}'. Error: {Message}", runRefId, ex.Message);
            _logger.LogTrace(ex, "GetDetailAsync exception stack trace for '{RunRefId}'", runRefId);
            return Result<RunDetailDto>.Failure(Error.Failure("RUN_QUERY_ERROR", ex.Message));
        }
    }

    public async Task<Result> CancelAsync(int workspaceId, Guid runRefId, string reason, CancellationToken ct)
    {
        _logger.LogInformation("Cancelling run RunRefId: '{RunRefId}' in WorkspaceId: {WorkspaceId} (Reason: '{Reason}')", runRefId, workspaceId, reason);

        try
        {
            var ensureRunResult = await EnsureRunInWorkspaceAsync(workspaceId, runRefId, ct);
            if (ensureRunResult.IsFailure)
            {
                return ensureRunResult;
            }

            await _cancellationService.RequestCancellationAsync(ensureRunResult.Value, reason, ct);
            _logger.LogInformation("Run cancellation requested successfully for RunRefId: '{RunRefId}'", runRefId);
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError("Unexpected error cancelling run RunRefId: '{RunRefId}'. Error: {Message}", runRefId, ex.Message);
            _logger.LogTrace(ex, "CancelAsync exception stack trace for '{RunRefId}'", runRefId);
            return Result.Failure(Error.Failure("RUN_CANCEL_ERROR", ex.Message));
        }
    }

    public async Task<Result<int>> EnsureRunInWorkspaceAsync(int workspaceId, Guid runRefId, CancellationToken ct)
    {
        _logger.LogDebug("Verifying run '{RunRefId}' belongs to WorkspaceId: {WorkspaceId}", runRefId, workspaceId);

        try
        {
            RunRow run = await _runProvider.GetByRefIdAsync(runRefId, ct);
            var ensureResult = await EnsureRunRowInWorkspaceAsync(workspaceId, run, ct);
            if (ensureResult.IsFailure)
            {
                return Result<int>.Failure(ensureResult.Error);
            }
            return Result<int>.Success(run.Id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed run membership check: run '{RunRefId}' not found. Error: {Message}", runRefId, ex.Message);
            return Result<int>.Failure(Error.NotFound("RUN_NOT_FOUND", "Run not found."));
        }
    }

    private async Task<Result> EnsureRunRowInWorkspaceAsync(int workspaceId, RunRow run, CancellationToken ct)
    {
        try
        {
            WorkflowDefinitionRow definition = await _workflowDefinitionProvider.GetByRefIdVersionAsync(run.WorkflowRefId, run.WorkflowVersion, ct);
            if (definition.WorkspaceId != workspaceId)
            {
                _logger.LogWarning("Workspace access denied for Run '{RunRefId}'", run.RefId);
                return Result.Failure(Error.Unauthorized("RUN_UNAUTHORIZED", $"Run '{run.RefId}' does not belong to the workspace."));
            }
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError("Error resolving workflow definition version {Version} for run '{RunRefId}'. Error: {Message}", run.WorkflowVersion, run.RefId, ex.Message);
            return Result.Failure(Error.NotFound("WORKFLOW_NOT_FOUND", "Workflow definition version associated with the run not found."));
        }
    }

    private async Task<Result> EnsureWorkflowInWorkspaceAsync(int workspaceId, Guid workflowRefId, CancellationToken ct)
    {
        try
        {
            WorkflowDefinitionRow definition = await _workflowDefinitionProvider.GetCurrentByRefIdAsync(workflowRefId, ct);
            if (definition.WorkspaceId != workspaceId)
            {
                _logger.LogWarning("Workspace access denied for workflow '{WorkflowRefId}'", workflowRefId);
                return Result.Failure(Error.Unauthorized("WORKFLOW_UNAUTHORIZED", $"Workflow '{workflowRefId}' does not belong to the workspace."));
            }
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Workflow not found for RefId: '{WorkflowRefId}'. Error: {Message}", workflowRefId, ex.Message);
            return Result.Failure(Error.NotFound("WORKFLOW_NOT_FOUND", "Workflow not found."));
        }
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
