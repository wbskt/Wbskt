using Microsoft.Data.SqlClient;
using System.Text.Json;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Workflow;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Security;
using Wbskt.Primitives.Exceptions;
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
    private const int WorkflowOwnedElsewhereError = 50021; // THROW in dbo.WorkflowDefinition_Publish
    private const int WorkflowDeletedError = 50022; // THROW in dbo.WorkflowDefinition_Publish
    private const string DeletedCancellationReason = "Workflow deleted.";

    private readonly IWorkflowDefinitionProvider _workflowDefinitionProvider;
    private readonly ITriggerRegistrationService _triggerRegistrationService;
    private readonly IWorkflowDefinitionCache _cache;
    private readonly WorkflowValidator _validator;
    private readonly IIdentityService _identityService;
    private readonly IRunCancellationService _runCancellation;
    private readonly IEventBus _eventBus;
    private readonly ILogger<WorkflowDefinitionService> _logger;

    public WorkflowDefinitionService(
        IWorkflowDefinitionProvider workflowDefinitionProvider,
        ITriggerRegistrationService triggerRegistrationService,
        IWorkflowDefinitionCache cache,
        WorkflowValidator validator,
        IIdentityService identityService,
        IRunCancellationService runCancellation,
        IEventBus eventBus,
        ILogger<WorkflowDefinitionService> logger)
    {
        _eventBus = eventBus;
        _runCancellation = runCancellation;
        _workflowDefinitionProvider = workflowDefinitionProvider;
        _triggerRegistrationService = triggerRegistrationService;
        _cache = cache;
        _validator = validator;
        _identityService = identityService;
        _logger = logger;
    }

    public Task<Result<WorkflowPublishResponse>> PublishAsync(int workspaceId, Guid workspaceRef, WorkflowPublishRequest request, CancellationToken ct)
    {
        return PublishCoreAsync(workspaceId, workspaceRef, request, restoredFromVersion: null, ct);
    }

    private async Task<Result<WorkflowPublishResponse>> PublishCoreAsync(int workspaceId, Guid workspaceRef, WorkflowPublishRequest request, int? restoredFromVersion, CancellationToken ct)
    {
        _logger.LogInformation("Publishing workflow '{WorkflowName}' (RefId: '{RefId}') in WorkspaceId: {WorkspaceId}", request.Name, request.RefId, workspaceId);

        try
        {
            ValidationResult validation = _validator.Validate(request.Definition);
            if (!validation.IsValid)
            {
                // Each error names its node, so the message points at what to fix rather than being a
                // bare sentence. The structured form is available from the validate endpoint.
                string message = string.Join("; ", validation.Issues
                    .Where(issue => issue.Severity == ValidationSeverity.Error)
                    .Select(issue => issue.NodeId is Guid nodeId
                        ? $"[{issue.Code}] {issue.Message} (node {nodeId})"
                        : $"[{issue.Code}] {issue.Message}"));
                _logger.LogWarning("Workflow publish failed: Validation issues: {Issues}", message);
                return Result<WorkflowPublishResponse>.Failure(Error.Validation("WORKFLOW_VALIDATION_FAILED", message));
            }

            WorkflowDefinitionRow? existing;
            try
            {
                existing = await _workflowDefinitionProvider.GetCurrentByRefIdAsync(request.RefId, ct);
            }
            catch (Exception ex) when (IsNotFound(ex))
            {
                // Only a genuine "no such workflow" means this is the first version. Swallowing every
                // exception here would let a transient DB error masquerade as a new workflow and skip
                // the workspace-ownership check below.
                existing = null;
            }

            if (existing is not null && existing.WorkspaceId != workspaceId)
            {
                _logger.LogWarning("Workflow publish rejected: Workflow '{RefId}' does not belong to WorkspaceId: {WorkspaceId}", request.RefId, workspaceId);
                return Result<WorkflowPublishResponse>.Failure(Error.Forbidden("WORKFLOW_UNAUTHORIZED", $"Workflow '{request.RefId}' does not belong to the workspace."));
            }

            // The version is assigned by WorkflowDefinition_Publish under HOLDLOCK, which also stamps
            // it into the stored JSON. Nothing is pre-computed here: a version guessed from the
            // unlocked read above could disagree with the row under concurrent publishes.
            WorkflowDefinitionRow inserted;
            try
            {
                inserted = await _workflowDefinitionProvider.InsertAsync(new WorkflowDefinitionRow
                {
                    Id = 0,
                    RefId = request.RefId,
                    Version = 0,
                    WorkspaceId = workspaceId,
                    Name = request.Name,
                    Description = request.Description,
                    IsEnabled = true,
                    DefinitionJson = JsonSerializer.Serialize(request.Definition with { WorkspaceId = workspaceId, IsEnabled = true }, SerializerOptions),
                    PublishedBy = _identityService.GetUserIdentity().UserId,
                    CreatedAt = request.Definition.CreatedAt
                }, ct);
            }
            catch (SqlException ex) when (ex.Number == WorkflowDeletedError)
            {
                // The current-version read above already treats a deleted workflow as missing, so this
                // is where a publish (or a rollback) under a deleted RefId ends.
                _logger.LogWarning("Workflow publish rejected: Workflow '{RefId}' was deleted", request.RefId);
                return Result<WorkflowPublishResponse>.Failure(Error.Conflict("WORKFLOW_DELETED", $"Workflow '{request.RefId}' was deleted and cannot be published again."));
            }
            catch (SqlException ex) when (ex.Number == WorkflowOwnedElsewhereError)
            {
                // Another workspace's first publish of the same RefId won the race past the check above.
                _logger.LogWarning("Workflow publish rejected under lock: Workflow '{RefId}' belongs to another workspace", request.RefId);
                return Result<WorkflowPublishResponse>.Failure(Error.Forbidden("WORKFLOW_UNAUTHORIZED", $"Workflow '{request.RefId}' does not belong to the workspace."));
            }

            // From here the row exists. Anything that fails must not leave a published version whose
            // triggers were never registered - that workflow would be current, and dead.
            try
            {
                if (existing is not null)
                {
                    await _workflowDefinitionProvider.DeprecateAsync(existing.Id, ct);
                    await _triggerRegistrationService.OnDeprecatedAsync(existing.Id, ct);
                    _cache.Invalidate(existing.Id);
                }

                await _triggerRegistrationService.OnPublishedAsync(inserted.Id, workspaceRef, ct);
                _cache.Invalidate(inserted.Id);
            }
            catch (Exception ex)
            {
                await CompensateFailedPublishAsync(inserted, existing, workspaceRef, ex, ct);
                throw;
            }

            _logger.LogInformation("Workflow '{WorkflowName}' (RefId: '{RefId}') version {Version} published successfully", request.Name, request.RefId, inserted.Version);
            await _eventBus.PublishAsync(new WorkflowPublishedEvent(inserted.RefId, inserted.Id, workspaceId, inserted.Version, request.Name, restoredFromVersion), ct);
            return Result<WorkflowPublishResponse>.Success(new WorkflowPublishResponse(inserted.RefId, inserted.Version, "Published"));
        }
        catch (Exception ex)
        {
            _logger.LogError("Unexpected error publishing workflow '{RefId}'. Error: {Message}", request.RefId, ex.Message);
            _logger.LogTrace(ex, "PublishAsync exception stack trace for RefId '{RefId}'", request.RefId);
            return Result<WorkflowPublishResponse>.Failure(Error.Failure("WORKFLOW_PUBLISH_ERROR", ex.Message));
        }
    }

    public async Task<Result> ReinstateAsync(int workspaceId, Guid workspaceRef, Guid refId, CancellationToken ct)
    {
        _logger.LogInformation("Reinstating workflow '{RefId}' in WorkspaceId: {WorkspaceId}", refId, workspaceId);

        try
        {
            WorkflowDefinitionRow row;
            try
            {
                row = await _workflowDefinitionProvider.GetCurrentByRefIdAsync(refId, ct);
            }
            catch (Exception ex) when (IsNotFound(ex))
            {
                _logger.LogWarning("Failed to reinstate: workflow '{RefId}' not found.", refId);
                return Result.Failure(Error.NotFound("WORKFLOW_NOT_FOUND", "Workflow not found."));
            }

            var ensureWorkspaceResult = EnsureWorkspace(row, workspaceId, refId);
            if (ensureWorkspaceResult.IsFailure)
            {
                return ensureWorkspaceResult;
            }

            if (row.IsEnabled)
            {
                return Result.Failure(Error.Conflict("WORKFLOW_ALREADY_ENABLED", $"Workflow '{refId}' is already enabled."));
            }

            // Deprecating deregisters the triggers, so re-enabling has to put them back - otherwise the
            // workflow reads as published and never fires. Registering first means a failure leaves the
            // row still disabled, which is the state the caller already had.
            await _triggerRegistrationService.OnPublishedAsync(row.Id, workspaceRef, ct);

            try
            {
                await _workflowDefinitionProvider.SetEnabledAsync(row.Id, true, ct);
            }
            catch
            {
                // Registrations without an enabled definition would fire a workflow the operator
                // believes is off - worse than leaving it off.
                await TryDeregisterAsync(row.Id, ct);
                throw;
            }

            _cache.Invalidate(row.Id);
            _logger.LogInformation("Workflow '{RefId}' version {Version} reinstated", refId, row.Version);
            await _eventBus.PublishAsync(new WorkflowReinstatedEvent(row.RefId, row.Id, workspaceId, row.Version), ct);
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError("Unexpected error reinstating workflow '{RefId}'. Error: {Message}", refId, ex.Message);
            _logger.LogTrace(ex, "ReinstateAsync exception stack trace for RefId '{RefId}'", refId);
            return Result.Failure(Error.Failure("WORKFLOW_REINSTATE_ERROR", ex.Message));
        }
    }

    public async Task<Result<WorkflowPublishResponse>> RollbackAsync(int workspaceId, Guid workspaceRef, Guid refId, int version, CancellationToken ct)
    {
        _logger.LogInformation("Rolling workflow '{RefId}' back to version {Version} in WorkspaceId: {WorkspaceId}", refId, version, workspaceId);

        WorkflowDefinitionRow source;
        try
        {
            source = await _workflowDefinitionProvider.GetByRefIdVersionAsync(refId, version, ct);
        }
        catch (Exception ex) when (IsNotFound(ex))
        {
            _logger.LogWarning("Failed to roll back: workflow '{RefId}' version {Version} not found.", refId, version);
            return Result<WorkflowPublishResponse>.Failure(Error.NotFound("WORKFLOW_VERSION_NOT_FOUND", $"Workflow '{refId}' has no version {version}."));
        }

        var ensureWorkspaceResult = EnsureWorkspace(source, workspaceId, refId);
        if (ensureWorkspaceResult.IsFailure)
        {
            return Result<WorkflowPublishResponse>.Failure(ensureWorkspaceResult.Error);
        }

        WorkflowDefinition? definition;
        try
        {
            definition = JsonSerializer.Deserialize<WorkflowDefinition>(source.DefinitionJson, SerializerOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogError("Cannot roll back workflow '{RefId}' to version {Version}: stored definition is unreadable. Error: {Message}", refId, version, ex.Message);
            return Result<WorkflowPublishResponse>.Failure(Error.Failure("WORKFLOW_DEFINITION_UNREADABLE", $"Version {version} of workflow '{refId}' could not be deserialized."));
        }

        if (definition is null)
        {
            return Result<WorkflowPublishResponse>.Failure(Error.Failure("WORKFLOW_DEFINITION_UNREADABLE", $"Version {version} of workflow '{refId}' could not be deserialized."));
        }

        // Rolling back publishes the old definition as a NEW version rather than resurrecting the old
        // row, so the version history stays append-only and the runs of every version keep pointing at
        // the definition they actually ran. Going through PublishAsync also means a rollback is
        // validated, versioned, registered and compensated on failure exactly like any other publish -
        // which matters, because a definition published before a validation rule existed may no longer
        // be valid.
        return await PublishCoreAsync(
            workspaceId,
            workspaceRef,
            new WorkflowPublishRequest(refId, source.Name, source.Description, definition),
            restoredFromVersion: version,
            ct);
    }

    private async Task TryDeregisterAsync(int definitionId, CancellationToken ct)
    {
        try
        {
            await _triggerRegistrationService.OnDeprecatedAsync(definitionId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deregister triggers for definition {DefinitionId} while undoing a reinstate.", definitionId);
        }
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

    /// <summary>
    /// Undoes a publish that inserted its row and then failed. Without this the new version stays
    /// current with no triggers registered - and if it superseded an earlier version, that one has
    /// already been deprecated and deregistered, so the workflow stops firing altogether.
    ///
    /// Best-effort and fully guarded: the original failure is what the caller reports, so a problem
    /// here must not replace it. Anything left behind is logged loudly enough to fix by hand.
    /// </summary>
    private async Task CompensateFailedPublishAsync(
        WorkflowDefinitionRow inserted,
        WorkflowDefinitionRow? existing,
        Guid workspaceRef,
        Exception cause,
        CancellationToken ct)
    {
        _logger.LogError(
            "Publish of workflow '{RefId}' version {Version} failed after the row was inserted ({Message}); rolling back.",
            inserted.RefId, inserted.Version, cause.Message);

        try
        {
            // Drop any registrations the failed attempt managed to create before throwing.
            await _triggerRegistrationService.OnDeprecatedAsync(inserted.Id, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Rollback: could not deregister triggers for workflow definition {Id}.", inserted.Id);
        }

        try
        {
            bool removed = await _workflowDefinitionProvider.DeleteUnreferencedAsync(inserted.Id, ct);
            if (!removed)
            {
                _logger.LogError(
                    "Rollback: workflow definition {Id} could not be removed (runs already reference it); it remains as a version with no triggers.",
                    inserted.Id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Rollback: could not remove workflow definition {Id}.", inserted.Id);
        }

        if (existing is null)
        {
            return;
        }

        // Put the superseded version back the way it was.
        try
        {
            await _workflowDefinitionProvider.SetEnabledAsync(existing.Id, true, ct);
            // The real workspace ref matters: webhook trigger keys are workspace-scoped, so
            // re-registering with anything else would mint keys nothing can ever match.
            await _triggerRegistrationService.OnPublishedAsync(existing.Id, workspaceRef, ct);
            _cache.Invalidate(existing.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Rollback: could not restore previous workflow definition {Id}; it may be left deprecated with no triggers.",
                existing.Id);
        }
    }

    /// <summary>Distinguishes "no such workflow" from a real failure talking to the database.</summary>
    private static bool IsNotFound(Exception ex)
    {
        return ex is KeyNotFoundException or NotFoundException;
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
            await _eventBus.PublishAsync(new WorkflowDeprecatedEvent(row.RefId, row.Id, workspaceId, row.Version), ct);
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError("Unexpected error deprecating workflow '{RefId}'. Error: {Message}", refId, ex.Message);
            _logger.LogTrace(ex, "DeprecateAsync exception stack trace for RefId '{RefId}'", refId);
            return Result.Failure(Error.Failure("WORKFLOW_DEPRECATE_ERROR", ex.Message));
        }
    }

    public async Task<Result<IReadOnlyList<WorkflowVersionDto>>> GetVersionsAsync(int workspaceId, Guid refId, CancellationToken ct)
    {
        _logger.LogDebug("Querying versions of workflow '{RefId}' in WorkspaceId: {WorkspaceId}", refId, workspaceId);

        try
        {
            var rows = await _workflowDefinitionProvider.GetVersionsAsync(refId, workspaceId, ct);
            if (rows.Count == 0)
            {
                // Unknown, deleted, or another workspace's: the procedure is workspace-scoped, so all
                // three read alike, as for a client or run reference that does not resolve here.
                return Result<IReadOnlyList<WorkflowVersionDto>>.Failure(Error.NotFound("WORKFLOW_NOT_FOUND", "Workflow not found."));
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
        catch (Exception ex)
        {
            _logger.LogError("Failed to query versions of workflow '{RefId}'. Error: {Message}", refId, ex.Message);
            _logger.LogTrace(ex, "GetVersionsAsync exception stack trace for RefId '{RefId}'", refId);
            return Result<IReadOnlyList<WorkflowVersionDto>>.Failure(Error.Failure("WORKFLOW_QUERY_ERROR", ex.Message));
        }
    }

    public async Task<Result> DeleteAsync(int workspaceId, Guid refId, CancellationToken ct)
    {
        _logger.LogInformation("Deleting workflow '{RefId}' in WorkspaceId: {WorkspaceId}", refId, workspaceId);

        try
        {
            var deletion = await _workflowDefinitionProvider.DeleteAsync(refId, workspaceId, _identityService.GetUserIdentity().UserId, ct);
            if (deletion is null)
            {
                // Same answer for unknown, already deleted, and another workspace's workflow.
                return Result.Failure(Error.NotFound("WORKFLOW_NOT_FOUND", "Workflow not found."));
            }

            // The triggers are gone, so nothing new starts; runs already going are stopped rather than
            // left to finish a workflow the operator has removed.
            foreach (long runId in deletion.ActiveRunIds)
            {
                await _runCancellation.RequestCancellationAsync(runId, DeletedCancellationReason, ct);
            }

            foreach (int definitionId in deletion.DefinitionIds)
            {
                _cache.Invalidate(definitionId);
            }

            _logger.LogInformation("Workflow '{RefId}' deleted: {Versions} version(s) disabled, {Runs} run(s) cancelling", refId, deletion.DefinitionIds.Count, deletion.ActiveRunIds.Count);
            await _eventBus.PublishAsync(new WorkflowDeletedEvent(refId, deletion.DefinitionIds.DefaultIfEmpty().Max(), workspaceId, deletion.ActiveRunIds.Count), ct);
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError("Unexpected error deleting workflow '{RefId}'. Error: {Message}", refId, ex.Message);
            _logger.LogTrace(ex, "DeleteAsync exception stack trace for RefId '{RefId}'", refId);
            return Result.Failure(Error.Failure("WORKFLOW_DELETE_ERROR", ex.Message));
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
