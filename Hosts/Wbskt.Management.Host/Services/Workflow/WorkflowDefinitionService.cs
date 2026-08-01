using System.Text.Json;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Security;
using Wbskt.Management.Models.Workflow;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Abstraction.Validation;

namespace Wbskt.Management.Host.Services.Workflow;

public sealed class WorkflowDefinitionService : IWorkflowDefinitionService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IWorkflowDefinitionProvider _workflowDefinitionProvider;
    private readonly ITriggerRegistrationService _triggerRegistrationService;
    private readonly IWorkflowDefinitionCache _cache;
    private readonly WorkflowValidator _validator;
    private readonly IIdentityService _identityService;
    private readonly ILogger<WorkflowDefinitionService> _logger;

    public WorkflowDefinitionService(
        IWorkflowDefinitionProvider workflowDefinitionProvider,
        ITriggerRegistrationService triggerRegistrationService,
        IWorkflowDefinitionCache cache,
        WorkflowValidator validator,
        IIdentityService identityService,
        ILogger<WorkflowDefinitionService> logger)
    {
        _workflowDefinitionProvider = workflowDefinitionProvider;
        _triggerRegistrationService = triggerRegistrationService;
        _cache = cache;
        _validator = validator;
        _identityService = identityService;
        _logger = logger;
    }

    public async Task<Result<WorkflowPublishResponse>> PublishAsync(int workspaceId, Guid workspaceRef, WorkflowPublishRequest request, CancellationToken ct)
    {
        _logger.LogInformation("Publishing workflow '{WorkflowName}' (RefId: '{RefId}') in WorkspaceId: {WorkspaceId}", request.Name, request.RefId, workspaceId);

        try
        {
            ValidationResult validation = _validator.Validate(request.Definition);
            if (!validation.IsValid)
            {
                string message = string.Join("; ", validation.Issues
                    .Where(issue => issue.Severity == ValidationSeverity.Error)
                    .Select(issue => issue.Message));
                _logger.LogWarning("Workflow publish failed: Validation issues: {Issues}", message);
                return Result<WorkflowPublishResponse>.Failure(Error.Validation("WORKFLOW_VALIDATION_FAILED", message));
            }

            WorkflowDefinitionRow? existing;
            try
            {
                existing = await _workflowDefinitionProvider.GetCurrentByRefIdAsync(request.RefId, ct);
            }
            catch (Exception)
            {
                existing = null;
            }

            int nextVersion = existing?.Version + 1 ?? 1;

            if (existing is not null && existing.WorkspaceId != workspaceId)
            {
                _logger.LogWarning("Workflow publish rejected: Workflow '{RefId}' does not belong to WorkspaceId: {WorkspaceId}", request.RefId, workspaceId);
                return Result<WorkflowPublishResponse>.Failure(Error.Forbidden("WORKFLOW_UNAUTHORIZED", $"Workflow '{request.RefId}' does not belong to the workspace."));
            }

            WorkflowDefinitionRow inserted = await _workflowDefinitionProvider.InsertAsync(new WorkflowDefinitionRow
            {
                Id = 0,
                RefId = request.RefId,
                Version = nextVersion,
                WorkspaceId = workspaceId,
                Name = request.Name,
                Description = request.Description,
                IsEnabled = true,
                DefinitionJson = JsonSerializer.Serialize(request.Definition with { WorkspaceId = workspaceId, Version = nextVersion, IsEnabled = true }, SerializerOptions),
                PublishedBy = _identityService.GetUserIdentity().UserId,
                CreatedAt = request.Definition.CreatedAt
            }, ct);

            if (existing is not null)
            {
                await _workflowDefinitionProvider.DeprecateAsync(existing.Id, ct);
                await _triggerRegistrationService.OnDeprecatedAsync(existing.Id, ct);
                _cache.Invalidate(existing.Id);
            }

            await _triggerRegistrationService.OnPublishedAsync(inserted.Id, workspaceRef, ct);
            _cache.Invalidate(inserted.Id);

            _logger.LogInformation("Workflow '{WorkflowName}' (RefId: '{RefId}') version {Version} published successfully", request.Name, request.RefId, nextVersion);
            return Result<WorkflowPublishResponse>.Success(new WorkflowPublishResponse(inserted.RefId, inserted.Version, "Published"));
        }
        catch (Exception ex)
        {
            _logger.LogError("Unexpected error publishing workflow '{RefId}'. Error: {Message}", request.RefId, ex.Message);
            _logger.LogTrace(ex, "PublishAsync exception stack trace for RefId '{RefId}'", request.RefId);
            return Result<WorkflowPublishResponse>.Failure(Error.Failure("WORKFLOW_PUBLISH_ERROR", ex.Message));
        }
    }

    public async Task<Result<WorkflowDefinitionDto>> GetCurrentAsync(int workspaceId, Guid refId, CancellationToken ct)
    {
        _logger.LogDebug("Querying current workflow definition for RefId: '{RefId}'", refId);

        try
        {
            WorkflowDefinitionRow row;
            try
            {
                row = await _workflowDefinitionProvider.GetCurrentByRefIdAsync(refId, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Workflow not found for RefId: '{RefId}'. Error: {Message}", refId, ex.Message);
                return Result<WorkflowDefinitionDto>.Failure(Error.NotFound("WORKFLOW_NOT_FOUND", "Workflow not found."));
            }

            var ensureWorkspaceResult = EnsureWorkspace(row, workspaceId, refId);
            if (ensureWorkspaceResult.IsFailure)
            {
                return Result<WorkflowDefinitionDto>.Failure(ensureWorkspaceResult.Error);
            }

            return Result<WorkflowDefinitionDto>.Success(Map(row));
        }
        catch (Exception ex)
        {
            _logger.LogError("Unexpected error querying current workflow for RefId: '{RefId}'. Error: {Message}", refId, ex.Message);
            _logger.LogTrace(ex, "GetCurrentAsync exception stack trace for RefId '{RefId}'", refId);
            return Result<WorkflowDefinitionDto>.Failure(Error.Failure("WORKFLOW_QUERY_ERROR", ex.Message));
        }
    }

    public async Task<Result<WorkflowDefinitionDto>> GetVersionAsync(int workspaceId, Guid refId, int version, CancellationToken ct)
    {
        _logger.LogDebug("Querying workflow definition for RefId: '{RefId}' version {Version}", refId, version);

        try
        {
            WorkflowDefinitionRow row;
            try
            {
                row = await _workflowDefinitionProvider.GetByRefIdVersionAsync(refId, version, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Workflow not found for RefId: '{RefId}' version {Version}. Error: {Message}", refId, version, ex.Message);
                return Result<WorkflowDefinitionDto>.Failure(Error.NotFound("WORKFLOW_NOT_FOUND", "Workflow not found."));
            }

            var ensureWorkspaceResult = EnsureWorkspace(row, workspaceId, refId);
            if (ensureWorkspaceResult.IsFailure)
            {
                return Result<WorkflowDefinitionDto>.Failure(ensureWorkspaceResult.Error);
            }

            return Result<WorkflowDefinitionDto>.Success(Map(row));
        }
        catch (Exception ex)
        {
            _logger.LogError("Unexpected error querying workflow for RefId: '{RefId}' version {Version}. Error: {Message}", refId, version, ex.Message);
            _logger.LogTrace(ex, "GetVersionAsync exception stack trace for RefId '{RefId}' version {Version}", refId, version);
            return Result<WorkflowDefinitionDto>.Failure(Error.Failure("WORKFLOW_QUERY_ERROR", ex.Message));
        }
    }

    public async Task<Result> DeprecateAsync(int workspaceId, Guid refId, CancellationToken ct)
    {
        _logger.LogInformation("Deprecating workflow '{RefId}' in WorkspaceId: {WorkspaceId}", refId, workspaceId);

        try
        {
            WorkflowDefinitionRow row;
            try
            {
                row = await _workflowDefinitionProvider.GetCurrentByRefIdAsync(refId, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to deprecate: workflow '{RefId}' not found. Error: {Message}", refId, ex.Message);
                return Result.Failure(Error.NotFound("WORKFLOW_NOT_FOUND", "Workflow not found."));
            }

            var ensureWorkspaceResult = EnsureWorkspace(row, workspaceId, refId);
            if (ensureWorkspaceResult.IsFailure)
            {
                return ensureWorkspaceResult;
            }

            await _workflowDefinitionProvider.DeprecateAsync(row.Id, ct);
            await _triggerRegistrationService.OnDeprecatedAsync(row.Id, ct);
            _cache.Invalidate(row.Id);

            _logger.LogInformation("Workflow '{RefId}' deprecated successfully", refId);
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError("Unexpected error deprecating workflow '{RefId}'. Error: {Message}", refId, ex.Message);
            _logger.LogTrace(ex, "DeprecateAsync exception stack trace for RefId '{RefId}'", refId);
            return Result.Failure(Error.Failure("WORKFLOW_DEPRECATE_ERROR", ex.Message));
        }
    }

    public async Task<Result<Wbskt.Models.IPagedList<WorkflowSummaryDto>>> GetAllSummariesAsync(int workspaceId, int skip, int take, CancellationToken ct)
    {
        _logger.LogDebug("Querying workflow summaries for WorkspaceId: {WorkspaceId}", workspaceId);

        try
        {
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
        catch (Exception ex)
        {
            _logger.LogError("Failed to query workflow summaries for WorkspaceId: {WorkspaceId}. Error: {Message}", workspaceId, ex.Message);
            _logger.LogTrace(ex, "GetAllSummariesAsync exception stack trace for WorkspaceId {WorkspaceId}", workspaceId);
            return Result<Wbskt.Models.IPagedList<WorkflowSummaryDto>>.Failure(Error.Failure("WORKFLOW_QUERY_ERROR", ex.Message));
        }
    }

    public async Task<Result> EnsureWorkflowInWorkspaceAsync(int workspaceId, Guid workflowRefId, CancellationToken ct)
    {
        _logger.LogDebug("Verifying workflow '{WorkflowRefId}' belongs to WorkspaceId: {WorkspaceId}", workflowRefId, workspaceId);

        try
        {
            WorkflowDefinitionRow row = await _workflowDefinitionProvider.GetCurrentByRefIdAsync(workflowRefId, ct);
            return EnsureWorkspace(row, workspaceId, workflowRefId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed workflow membership check: workflow '{WorkflowRefId}' not found. Error: {Message}", workflowRefId, ex.Message);
            return Result.Failure(Error.NotFound("WORKFLOW_NOT_FOUND", "Workflow not found."));
        }
    }

    private Result EnsureWorkspace(WorkflowDefinitionRow row, int workspaceId, Guid refId)
    {
        if (row.WorkspaceId != workspaceId)
        {
            _logger.LogWarning("Workspace access denied for workflow '{RefId}'", refId);
            return Result.Failure(Error.Forbidden("WORKFLOW_UNAUTHORIZED", $"Workflow '{refId}' does not belong to the workspace."));
        }
        return Result.Success();
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
