using Wbskt.Models;
using Wbskt.Management.Host.Services.Integrations;
using System.Text.Json;
using Wbskt.Infrastructure;
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
    private readonly IWorkflowStateProvider _states;
    private readonly WorkflowValidator _validator;
    private readonly IntegrationReferenceCheck _integrationCheck;
    private readonly ClientReferenceCheck? _clientCheck;
    private readonly SubWorkflowReferenceCheck? _subWorkflowCheck;
    private readonly ILogger<WorkflowQueryService> _logger;

    public WorkflowQueryService(
        IWorkflowDefinitionProvider workflowDefinitionProvider,
        IWorkflowStateProvider states,
        WorkflowValidator validator,
        IntegrationReferenceCheck integrationCheck,
        ILogger<WorkflowQueryService> logger,
        ClientReferenceCheck? clientCheck = null,
        SubWorkflowReferenceCheck? subWorkflowCheck = null)
    {
        _integrationCheck = integrationCheck;
        _clientCheck = clientCheck;
        _subWorkflowCheck = subWorkflowCheck;
        _workflowDefinitionProvider = workflowDefinitionProvider;
        _states = states;
        _validator = validator;
        _logger = logger;
    }

    public Task<WorkflowValidationResponse> ValidateAsync(int workspaceId, WorkflowDefinition? definition, CancellationToken ct)
    {
        return ValidateAsync(workspaceId, definition, refId: null, ct);
    }

    public Task<WorkflowValidationResponse> ValidateAsync(int workspaceId, WorkflowValidationRequest request, CancellationToken ct)
    {
        // Nothing is stored, so the definition's time is never read.
        return ValidateAsync(workspaceId, AuthoredDefinitions.From(request, now: default), request.RefId, ct);
    }

    private async Task<WorkflowValidationResponse> ValidateAsync(int workspaceId, WorkflowDefinition? definition, Guid? refId, CancellationToken ct)
    {
        ValidationResult validation = _validator.Validate(definition, refId);

        // The workspace's half - do the integrations, clients and child workflows the steps name exist
        // here - so a draft hears what publishing it would.
        IntegrationReferences integrations = await _integrationCheck.CheckAsync(workspaceId, definition, ct);
        IReadOnlyList<ValidationIssue> clients = _clientCheck is null ? [] : await _clientCheck.CheckAsync(workspaceId, definition, ct);
        IReadOnlyList<ValidationIssue> children = _subWorkflowCheck is null ? [] : await _subWorkflowCheck.CheckAsync(workspaceId, definition, ct);
        var issues = validation.Issues.Concat(integrations.Issues).Concat(clients).Concat(children).ToList();

        return new WorkflowValidationResponse(
            issues.All(issue => issue.Severity != ValidationSeverity.Error),
            issues
                .Select(issue => new WorkflowValidationIssueDto(
                    issue.Severity.ToString(),
                    issue.Code,
                    issue.Message,
                    issue.NodeId,
                    issue.Field))
                .ToList());
    }

    public async Task<Result<WorkflowDefinitionDto>> GetCurrentAsync(int workspaceId, Guid refId, CancellationToken ct)
    {
        _logger.LogDebug("Querying current workflow definition for RefId: '{RefId}'", refId);

        var load = await WorkflowOwnershipCheck.LoadCurrentAsync(_workflowDefinitionProvider, _logger, workspaceId, refId, ct);
        return load.IsFailure
            ? Result<WorkflowDefinitionDto>.Failure(load.Error)
            : Result<WorkflowDefinitionDto>.Success(Map(load.Value, await _states.FindAsync(refId, workspaceId, ct)));
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

        return Result<WorkflowDefinitionDto>.Success(Map(row, await _states.FindAsync(refId, workspaceId, ct)));
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

        var versions = rows.Select(r => new WorkflowVersionDto(
            r.Version,
            r.IsLive,
            r.Name,
            r.Description,
            r.RunCount,
            r.CreatedAt.ToUtcOffset(),
            r.Note)).ToList();

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
            row.State ?? throw new InvalidOperationException($"The summary of workflow '{row.RefId}' carries no state."),
            row.LiveVersion,
            row.Name,
            row.Description,
            row.CreatedAt.ToUtcOffset(),
            WorkflowTriggerKinds.FromNodeKinds(row.TriggerNodeKinds),
            row.Draft is null ? null : new WorkflowDraftInfoDto(row.Draft.Revision, row.Draft.BaseVersion, row.Draft.UpdatedByRefId, row.Draft.UpdatedAt.ToUtcOffset())
        )).ToList();

        var pagedResult = new Wbskt.Models.PagedList<WorkflowSummaryDto>(dtos, result.TotalCount) as Wbskt.Models.IPagedList<WorkflowSummaryDto>;
        return Result<Wbskt.Models.IPagedList<WorkflowSummaryDto>>.Success(pagedResult);
    }

    public async Task<Result> EnsureWorkflowInWorkspaceAsync(int workspaceId, Guid workflowRefId, CancellationToken ct)
    {
        _logger.LogDebug("Verifying workflow '{WorkflowRefId}' belongs to WorkspaceId: {WorkspaceId}", workflowRefId, workspaceId);

        return await WorkflowOwnershipCheck.EnsureCurrentAsync(_workflowDefinitionProvider, _logger, workspaceId, workflowRefId, ct);
    }

    private static WorkflowDefinitionDto Map(WorkflowDefinitionRow row, WorkflowStateRow? state)
    {
        WorkflowDefinition definition = JsonSerializer.Deserialize<WorkflowDefinition>(row.DefinitionJson, SerializerOptions)
            ?? throw new InvalidOperationException($"Could not deserialize workflow definition for '{row.RefId}'");
        
        definition = definition with { WorkspaceId = row.WorkspaceId, PublishedBy = row.PublishedBy };

        return new WorkflowDefinitionDto(
            row.RefId,
            row.Version,
            state?.State,
            state?.LiveVersion,
            row.Name,
            row.Description,
            definition,
            row.CreatedAt.ToUtcOffset());
    }
}
