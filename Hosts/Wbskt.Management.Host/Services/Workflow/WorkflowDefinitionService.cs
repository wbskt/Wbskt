using Microsoft.Data.SqlClient;
using System.Text.Json;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Workflow;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Security;
using Wbskt.Management.Host.Services.Clients;
using Wbskt.Management.Models.Workflow;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Abstraction.Validation;

namespace Wbskt.Management.Host.Services.Workflow;

// Publish, deprecate, reinstate, rollback and delete write definitions and trigger registrations
// directly, and evict nothing from any definition cache: this host keeps none, and the engine's cache
// is its own (evicting a copy here never reached it). A WorkflowDefinitionChanged event, for the engine
// to react to, is the planned follow-up in place of a shared cache contract.
public sealed class WorkflowDefinitionService : IWorkflowDefinitionService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private const int WorkflowOwnedElsewhereError = 50021; // THROW in dbo.WorkflowDefinition_Publish
    private const int WorkflowDeletedError = 50022; // THROW in dbo.WorkflowDefinition_Publish
    private const string DeletedCancellationReason = "Workflow deleted.";

    private readonly IWorkflowDefinitionProvider _workflowDefinitionProvider;
    private readonly ITriggerRegistrationService _triggerRegistrationService;
    private readonly WorkflowValidator _validator;
    private readonly IIdentityService _identityService;
    private readonly IWorkflowEngineGateway _engine;
    private readonly IEventBus _eventBus;
    private readonly ILogger<WorkflowDefinitionService> _logger;

    public WorkflowDefinitionService(
        IWorkflowDefinitionProvider workflowDefinitionProvider,
        ITriggerRegistrationService triggerRegistrationService,
        WorkflowValidator validator,
        IIdentityService identityService,
        IWorkflowEngineGateway engine,
        IEventBus eventBus,
        ILogger<WorkflowDefinitionService> logger)
    {
        _eventBus = eventBus;
        _engine = engine;
        _workflowDefinitionProvider = workflowDefinitionProvider;
        _triggerRegistrationService = triggerRegistrationService;
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

        // Only a genuine "no such workflow" means this is the first version. A database fault
        // propagates rather than masquerading as a new workflow and skipping the ownership check below.
        WorkflowDefinitionRow? existing = await _workflowDefinitionProvider.FindCurrentByRefIdAsync(request.RefId, ct);

        if (existing is not null && existing.WorkspaceId != workspaceId)
        {
            _logger.LogWarning("Workflow publish rejected: Workflow '{RefId}' does not belong to WorkspaceId: {WorkspaceId}", request.RefId, workspaceId);
            return Result<WorkflowPublishResponse>.Failure(WorkflowRefTaken);
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
            return Result<WorkflowPublishResponse>.Failure(WorkflowRefTaken);
        }

        // From here the row exists. Anything that fails must not leave a published version whose
        // triggers were never registered - that workflow would be current, and dead.
        try
        {
            if (existing is not null)
            {
                await _workflowDefinitionProvider.DeprecateAsync(existing.Id, ct);
                await _triggerRegistrationService.OnDeprecatedAsync(existing.Id, ct);
            }

            await _triggerRegistrationService.OnPublishedAsync(inserted.Id, workspaceRef, ct);
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

    public async Task<Result> ReinstateAsync(int workspaceId, Guid workspaceRef, Guid refId, CancellationToken ct)
    {
        _logger.LogInformation("Reinstating workflow '{RefId}' in WorkspaceId: {WorkspaceId}", refId, workspaceId);

        var load = await LoadCurrentAsync(workspaceId, refId, ct);
        if (load.IsFailure)
        {
            return Result.Failure(load.Error);
        }

        WorkflowDefinitionRow row = load.Value;
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

        _logger.LogInformation("Workflow '{RefId}' version {Version} reinstated", refId, row.Version);
        await _eventBus.PublishAsync(new WorkflowReinstatedEvent(row.RefId, row.Id, workspaceId, row.Version), ct);
        return Result.Success();
    }

    public async Task<Result<WorkflowPublishResponse>> RollbackAsync(int workspaceId, Guid workspaceRef, Guid refId, int version, CancellationToken ct)
    {
        _logger.LogInformation("Rolling workflow '{RefId}' back to version {Version} in WorkspaceId: {WorkspaceId}", refId, version, workspaceId);

        WorkflowDefinitionRow? source = await _workflowDefinitionProvider.FindRowByRefIdVersionAsync(refId, version, ct);
        if (source is null)
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
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Rollback: could not restore previous workflow definition {Id}; it may be left deprecated with no triggers.",
                existing.Id);
        }
    }

    /// <summary>
    /// A publish names its own RefId, so it can land on one another workspace already uses. That is
    /// a conflict to resolve by choosing another RefId, not a permission the caller lacks: inside a
    /// workspace a 403 only ever means a missing permission (see "The ID Boundary").
    /// </summary>
    private static readonly Error WorkflowRefTaken = Error.Conflict("WORKFLOW_REF_TAKEN", "This workflow reference is already in use. Publish under a new RefId.");

    public async Task<Result<WorkflowDefinitionDto>> GetCurrentAsync(int workspaceId, Guid refId, CancellationToken ct)
    {
        _logger.LogDebug("Querying current workflow definition for RefId: '{RefId}'", refId);

        var load = await LoadCurrentAsync(workspaceId, refId, ct);
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

        var ensureWorkspaceResult = EnsureWorkspace(row, workspaceId, refId);
        if (ensureWorkspaceResult.IsFailure)
        {
            return Result<WorkflowDefinitionDto>.Failure(ensureWorkspaceResult.Error);
        }

        return Result<WorkflowDefinitionDto>.Success(Map(row));
    }

    public async Task<Result> DeprecateAsync(int workspaceId, Guid refId, CancellationToken ct)
    {
        _logger.LogInformation("Deprecating workflow '{RefId}' in WorkspaceId: {WorkspaceId}", refId, workspaceId);

        var load = await LoadCurrentAsync(workspaceId, refId, ct);
        if (load.IsFailure)
        {
            return Result.Failure(load.Error);
        }

        WorkflowDefinitionRow row = load.Value;
        await _workflowDefinitionProvider.DeprecateAsync(row.Id, ct);
        await _triggerRegistrationService.OnDeprecatedAsync(row.Id, ct);

        _logger.LogInformation("Workflow '{RefId}' deprecated successfully", refId);
        await _eventBus.PublishAsync(new WorkflowDeprecatedEvent(row.RefId, row.Id, workspaceId, row.Version), ct);
        return Result.Success();
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

    public async Task<Result> DeleteAsync(int workspaceId, Guid refId, CancellationToken ct)
    {
        _logger.LogInformation("Deleting workflow '{RefId}' in WorkspaceId: {WorkspaceId}", refId, workspaceId);

        var deletion = await _workflowDefinitionProvider.DeleteAsync(refId, workspaceId, _identityService.GetUserIdentity().UserId, ct);
        if (deletion is null)
        {
            // Same answer for unknown, already deleted, and another workspace's workflow.
            return Result.Failure(WorkspaceOwnership.WorkflowNotFound);
        }

        // The triggers are gone, so nothing new starts; runs already going are stopped rather than
        // left to finish a workflow the operator has removed. The engine does the stopping. The
        // delete is committed by now, so the commands are queued: a broker outage delays the
        // cancels instead of turning a finished delete into a 500 that a retry would answer 404.
        foreach (long runId in deletion.ActiveRunIds)
        {
            await _engine.QueueCancelRunAsync(runId, DeletedCancellationReason, ct);
        }

        _logger.LogInformation("Workflow '{RefId}' deleted: {Versions} version(s) disabled, {Runs} run(s) cancelling", refId, deletion.DefinitionIds.Count, deletion.ActiveRunIds.Count);
        await _eventBus.PublishAsync(new WorkflowDeletedEvent(refId, deletion.DefinitionIds.DefaultIfEmpty().Max(), workspaceId, deletion.ActiveRunIds.Count), ct);
        return Result.Success();
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

        var load = await LoadCurrentAsync(workspaceId, workflowRefId, ct);
        return load.IsFailure ? Result.Failure(load.Error) : Result.Success();
    }

    /// <summary>
    /// The current version of a workflow <paramref name="workspaceId"/> owns. Unknown, deleted and
    /// another workspace's workflow all read as <see cref="WorkspaceOwnership.WorkflowNotFound"/>.
    /// </summary>
    private async Task<Result<WorkflowDefinitionRow>> LoadCurrentAsync(int workspaceId, Guid refId, CancellationToken ct)
    {
        WorkflowDefinitionRow? row = await _workflowDefinitionProvider.FindCurrentByRefIdAsync(refId, ct);
        if (row is null)
        {
            return Result<WorkflowDefinitionRow>.Failure(WorkspaceOwnership.WorkflowNotFound);
        }

        var ensureWorkspaceResult = EnsureWorkspace(row, workspaceId, refId);
        return ensureWorkspaceResult.IsFailure
            ? Result<WorkflowDefinitionRow>.Failure(ensureWorkspaceResult.Error)
            : Result<WorkflowDefinitionRow>.Success(row);
    }

    private Result EnsureWorkspace(WorkflowDefinitionRow row, int workspaceId, Guid refId)
    {
        if (row.WorkspaceId != workspaceId)
        {
            _logger.LogWarning("Workflow '{RefId}' is not in WorkspaceId: {WorkspaceId}", refId, workspaceId);
            return Result.Failure(WorkspaceOwnership.WorkflowNotFound);
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
