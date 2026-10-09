using System.Text.Json;
using Wbskt.Infrastructure;
using Wbskt.Management.Host.Services.Clients;
using Wbskt.Management.Models.Workflow;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Validation;

namespace Wbskt.Management.Host.Services.Workflow;

/// <summary>
/// Reads workflow definitions, and checks a draft without publishing it. Nothing here writes;
/// publishing and every other change of state is <see cref="WorkflowLifecycleService"/>'s.
/// </summary>
public sealed class WorkflowQueryService : IWorkflowQueryService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IWorkflowDefinitionProvider _workflowDefinitionProvider;
    private readonly WorkflowValidator _validator;
    private readonly ILogger<WorkflowQueryService> _logger;

    public WorkflowQueryService(
        IWorkflowDefinitionProvider workflowDefinitionProvider,
        WorkflowValidator validator,
        ILogger<WorkflowQueryService> logger)
    {
        _workflowDefinitionProvider = workflowDefinitionProvider;
        _validator = validator;
        _logger = logger;
    }

    public WorkflowValidationResponse Validate(WorkflowDefinition? definition)
    {
        ValidationResult validation = _validator.Validate(definition);

        return new WorkflowValidationResponse(
            validation.IsValid,
            validation.Issues
                .Select(issue => new WorkflowValidationIssueDto(
                    issue.Severity.ToString(),
                    issue.Code,
                    issue.Message,
                    issue.NodeId))
                .ToList());
    }

    public async Task<Result<WorkflowDefinitionDto>> GetCurrentAsync(int workspaceId, Guid refId, CancellationToken ct)
    {
        _logger.LogDebug("Querying current workflow definition for RefId: '{RefId}'", refId);

        var load = await WorkflowOwnershipCheck.LoadCurrentAsync(_workflowDefinitionProvider, _logger, workspaceId, refId, ct);
        return load.IsFailure
            ? Result<WorkflowDefinitionDto>.Failure(load.Error)
            : Result<WorkflowDefinitionDto>.Success(Map(load.Value));
    }

    public async Task<Result<WorkflowDefinitionDto>> GetVersionAsync(int workspaceId, Guid refId, int version, CancellationToken ct)
    {
        _logger.LogDebug("Querying workflow definition for RefId: '{RefId}' version {Version}", refId, version);

        WorkflowDefinitionRow? row = await _workflowDefinitionProvider.FindRowByRefIdVersionAsync(refId, version, ct);
        if (row is null)
        {
            return Result<WorkflowDefinitionDto>.Failure(WorkspaceOwnership.WorkflowNotFound);
        }

        var ensureWorkspaceResult = WorkflowOwnershipCheck.EnsureWorkspace(row, workspaceId, refId, _logger);
        if (ensureWorkspaceResult.IsFailure)
        {
            return Result<WorkflowDefinitionDto>.Failure(ensureWorkspaceResult.Error);
        }

        return Result<WorkflowDefinitionDto>.Success(Map(row));
    }

    public async Task<Result<IReadOnlyList<WorkflowVersionDto>>> GetVersionsAsync(int workspaceId, Guid refId, CancellationToken ct)
    {
        _logger.LogDebug("Querying versions of workflow '{RefId}' in WorkspaceId: {WorkspaceId}", refId, workspaceId);

        var rows = await _workflowDefinitionProvider.GetVersionsAsync(refId, workspaceId, ct);
        if (rows.Count == 0)
        {
            // Unknown, deleted, or another workspace's: the procedure is workspace-scoped, so all
            // three read alike, as for a client or run reference that does not resolve here.
            return Result<IReadOnlyList<WorkflowVersionDto>>.Failure(WorkspaceOwnership.WorkflowNotFound);
        }

        // Only the newest version can be live; an older one was superseded when the next was published.
        int current = rows.Max(r => r.Version);
        var versions = rows.Select(r => new WorkflowVersionDto(
            r.Version,
            r.Version != current ? "Superseded" : r.IsEnabled ? "Published" : "Deprecated",
            r.Name,
            r.Description,
            r.RunCount,
            r.CreatedAt)).ToList();

        return Result<IReadOnlyList<WorkflowVersionDto>>.Success(versions);
    }

    public async Task<Result<Wbskt.Models.IPagedList<WorkflowSummaryDto>>> GetAllSummariesAsync(int workspaceId, int skip, int take, CancellationToken ct)
    {
        _logger.LogDebug("Querying workflow summaries for WorkspaceId: {WorkspaceId}", workspaceId);

        var result = await _workflowDefinitionProvider.GetAllSummariesAsync(workspaceId, skip, take, ct);
        _logger.LogTrace("Retrieved {Count} workflow summaries for WorkspaceId: {WorkspaceId}", result.TotalCount, workspaceId);

        var dtos = result.Select(row => new WorkflowSummaryDto(
            row.RefId,
            row.Version,
            row.IsEnabled ? "Published" : "Deprecated",
            row.Name,
            row.Description,
            row.CreatedAt
        )).ToList();

        var pagedResult = new Wbskt.Models.PagedList<WorkflowSummaryDto>(dtos, result.TotalCount) as Wbskt.Models.IPagedList<WorkflowSummaryDto>;
        return Result<Wbskt.Models.IPagedList<WorkflowSummaryDto>>.Success(pagedResult);
    }

    public async Task<Result> EnsureWorkflowInWorkspaceAsync(int workspaceId, Guid workflowRefId, CancellationToken ct)
    {
        _logger.LogDebug("Verifying workflow '{WorkflowRefId}' belongs to WorkspaceId: {WorkspaceId}", workflowRefId, workspaceId);

        var load = await WorkflowOwnershipCheck.LoadCurrentAsync(_workflowDefinitionProvider, _logger, workspaceId, workflowRefId, ct);
        return load.IsFailure ? Result.Failure(load.Error) : Result.Success();
    }

    private static WorkflowDefinitionDto Map(WorkflowDefinitionRow row)
    {
        WorkflowDefinition definition = JsonSerializer.Deserialize<WorkflowDefinition>(row.DefinitionJson, SerializerOptions)
            ?? throw new InvalidOperationException($"Could not deserialize workflow definition for '{row.RefId}'");
        
        definition = definition with { WorkspaceId = row.WorkspaceId, PublishedBy = row.PublishedBy };

        return new WorkflowDefinitionDto(
            row.RefId,
            row.Version,
            row.IsEnabled ? "Published" : "Deprecated",
            row.Name,
            row.Description,
            definition,
            row.CreatedAt);
    }
}
