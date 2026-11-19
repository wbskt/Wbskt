using System.Globalization;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;
using Wbskt.Events.Workflow;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Security;
using Wbskt.Management.Host.Services.Clients;
using Wbskt.Management.Host.Services.Integrations;
using Wbskt.Management.Models.Workflow;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Abstraction.Validation;

namespace Wbskt.Management.Host.Services.Workflow;

// Every state change is one procedure, one transaction under the workflow's row lock, and the procedure
// decides whether it is allowed (WorkflowLifecycleErrors maps its refusals). Publishing only adds a
// version; making one live is Workflow_Enable, which swaps the trigger registrations, schedules and
// queue with the state. Every state in between is a valid one, so nothing here undoes a step.
//
// This host caches neither definitions nor registrations; the engine caches both, and is told to drop
// them by a WorkflowDefinitionChangedEvent published after each change. The event is queued like the
// others, so the engine also keeps its entries for a bounded time only.
public sealed class WorkflowLifecycleService : IWorkflowLifecycleService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public const int DefaultMaxPerWorkspace = 100;
    private const string DeletedCancellationReason = "Workflow deleted.";
    private const string DisabledCancellationReason = "Workflow disabled.";

    private readonly TimeProvider _clock;
    private readonly IWorkflowDefinitionProvider _definitions;
    private readonly IWorkflowStateProvider _states;
    private readonly ITriggerPlanner _planner;
    private readonly WorkflowValidator _validator;
    private readonly IntegrationReferenceCheck _integrationCheck;
    private readonly IIdentityService _identityService;
    private readonly IWorkflowEngineGateway _engine;
    private readonly IEventBus _eventBus;
    private readonly ILogger<WorkflowLifecycleService> _logger;
    private readonly IConfiguration? _configuration;
    private readonly ClientReferenceCheck? _clientCheck;
    private readonly SubWorkflowReferenceCheck? _subWorkflowCheck;

    public WorkflowLifecycleService(
        IWorkflowDefinitionProvider definitions,
        IWorkflowStateProvider states,
        ITriggerPlanner planner,
        WorkflowValidator validator,
        IntegrationReferenceCheck integrationCheck,
        IIdentityService identityService,
        IWorkflowEngineGateway engine,
        IEventBus eventBus,
        ILogger<WorkflowLifecycleService> logger,
        IConfiguration? configuration = null,
        ClientReferenceCheck? clientCheck = null,
        SubWorkflowReferenceCheck? subWorkflowCheck = null,
        TimeProvider? clock = null)
    {
        _clock = clock ?? TimeProvider.System;
        _definitions = definitions;
        _states = states;
        _planner = planner;
        _validator = validator;
        _integrationCheck = integrationCheck;
        _identityService = identityService;
        _engine = engine;
        _eventBus = eventBus;
        _logger = logger;
        _configuration = configuration;
        _clientCheck = clientCheck;
        _subWorkflowCheck = subWorkflowCheck;
    }

    // Configuration, not a constant, so it can become a plan tier without a redeploy of the code.
    private int MaxPerWorkspace => _configuration?.GetValue("Workflows:MaxPerWorkspace", DefaultMaxPerWorkspace) ?? DefaultMaxPerWorkspace;

    private int CurrentUserId => _identityService.GetUserIdentity().UserId;

    public Task<Result<WorkflowPublishResponse>> PublishAsync(int workspaceId, Guid workspaceRef, WorkflowPublishRequest request, CancellationToken ct)
    {
        return PublishAsync(workspaceId, workspaceRef, request, WorkflowPublishGuards.None, ct);
    }

    public async Task<Result<WorkflowPublishResponse>> PublishAsync(int workspaceId, Guid workspaceRef, WorkflowPublishRequest request, WorkflowPublishGuards guards, CancellationToken ct)
    {
        _logger.LogInformation("Publishing workflow '{WorkflowName}' (RefId: '{RefId}') in WorkspaceId: {WorkspaceId}, enable: {Enable}", request.Name, request.RefId, workspaceId, request.Enable);

        DateTime now = _clock.GetUtcNow().UtcDateTime;
        WorkflowDefinition? definition = AuthoredDefinitions.From(request, now);
        Result<IntegrationReferences> check = await CheckAsync(workspaceId, request.RefId, definition, ct);
        if (check.IsFailure)
        {
            return Result<WorkflowPublishResponse>.Failure(check.Error);
        }

        if (definition is null)
        {
            throw new InvalidOperationException("The validator passed a publish with no definition.");
        }

        // Only a genuine "no such workflow" means this is the first version. A database fault
        // propagates rather than masquerading as a new workflow and skipping the ownership check below.
        WorkflowDefinitionRow? existing = await _definitions.FindCurrentByRefIdAsync(request.RefId, ct);
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
            inserted = await _definitions.InsertAsync(new WorkflowDefinitionRow
            {
                Id = 0,
                RefId = request.RefId,
                Version = 0,
                WorkspaceId = workspaceId,
                Name = request.Name,
                Description = request.Description,
                DefinitionJson = JsonSerializer.Serialize(definition with { WorkspaceId = workspaceId }, SerializerOptions),
                PublishedBy = CurrentUserId,
                CreatedAt = now
            }, check.Value.Refs, MaxPerWorkspace, guards, ct);
        }
        catch (SqlException ex) when (ex.Number == WorkflowLifecycleErrors.VersionMoved)
        {
            _logger.LogInformation("Workflow publish refused: Workflow '{RefId}' is no longer at version {Version}", request.RefId, guards.ExpectedCurrentVersion);
            return Result<WorkflowPublishResponse>.Failure(WorkflowDraftErrors.Outdated);
        }
        catch (SqlException ex) when (ex.Number == WorkflowLifecycleErrors.LimitReached)
        {
            _logger.LogWarning("Workflow publish rejected: WorkspaceId {WorkspaceId} is at its limit of {MaxWorkflows} workflows", workspaceId, MaxPerWorkspace);
            return Result<WorkflowPublishResponse>.Failure(Error.Conflict(
                "WORKFLOW_LIMIT_REACHED",
                $"A workspace can have at most {MaxPerWorkspace} workflows. Delete one you no longer use."));
        }
        catch (SqlException ex) when (ex.Number == WorkflowLifecycleErrors.OwnedElsewhere)
        {
            // Another workspace's first publish of the same RefId won the race past the check above.
            _logger.LogWarning("Workflow publish rejected under lock: Workflow '{RefId}' belongs to another workspace", request.RefId);
            return Result<WorkflowPublishResponse>.Failure(WorkflowRefTaken);
        }
        catch (SqlException ex) when (WorkflowLifecycleErrors.TryMap(ex, request.RefId, null, out Error error))
        {
            // Deprecated, deleted, or an integration deleted between the check above and the insert.
            _logger.LogWarning("Workflow publish of '{RefId}' refused: {Code}", request.RefId, error.Code);
            return Result<WorkflowPublishResponse>.Failure(error);
        }

        _logger.LogInformation("Workflow '{WorkflowName}' (RefId: '{RefId}') version {Version} published", request.Name, request.RefId, inserted.Version);
        await _eventBus.PublishAsync(new WorkflowPublishedEvent(inserted.RefId, inserted.Id, workspaceId, inserted.Version, request.Name, request.Enable)
        {
            Changes = [new FieldChange("version", existing?.Version.ToString(CultureInfo.InvariantCulture), inserted.Version.ToString(CultureInfo.InvariantCulture))]
        }, ct);

        if (!request.Enable)
        {
            WorkflowStateRow? state = await _states.FindAsync(inserted.RefId, workspaceId, ct);
            return Result<WorkflowPublishResponse>.Success(new WorkflowPublishResponse(inserted.RefId, inserted.Version, state?.State ?? WorkflowState.Disabled, state?.LiveVersion));
        }

        // A second step on purpose: a version published but not live is a valid state, so a refused
        // enable leaves the previous live version running and the new one waiting to be enabled.
        Result<WorkflowStateResponse> enabled = await EnableCoreAsync(workspaceId, workspaceRef, inserted, definition, check.Value, ct);
        if (enabled.IsFailure)
        {
            _logger.LogWarning("Workflow '{RefId}' version {Version} was published but not made live: {Code}", inserted.RefId, inserted.Version, enabled.Error.Code);
            return Result<WorkflowPublishResponse>.Failure(enabled.Error with
            {
                Message = $"Version {inserted.Version} was published but not made live: {enabled.Error.Message}"
            });
        }

        return Result<WorkflowPublishResponse>.Success(new WorkflowPublishResponse(inserted.RefId, inserted.Version, enabled.Value.State, enabled.Value.LiveVersion));
    }

    public async Task<Result<WorkflowStateResponse>> EnableAsync(int workspaceId, Guid workspaceRef, Guid refId, int version, CancellationToken ct)
    {
        _logger.LogInformation("Enabling workflow '{RefId}' version {Version} in WorkspaceId: {WorkspaceId}", refId, version, workspaceId);

        // A workflow that is not this workspace's is WORKFLOW_NOT_FOUND, as on every other action; only
        // a version missing from a workflow the caller can see is WORKFLOW_VERSION_NOT_FOUND.
        WorkflowDefinitionRow? row = await _definitions.FindRowByRefIdVersionAsync(refId, version, ct);
        if (row is null || row.WorkspaceId != workspaceId)
        {
            bool visible = await _states.FindAsync(refId, workspaceId, ct) is not null;
            return Result<WorkflowStateResponse>.Failure(visible ? WorkflowLifecycleErrors.VersionNotFound(refId, version) : WorkspaceOwnership.WorkflowNotFound);
        }

        WorkflowDefinition? definition = JsonSerializer.Deserialize<WorkflowDefinition>(row.DefinitionJson, SerializerOptions);
        if (definition is null)
        {
            throw new InvalidOperationException($"Version {version} of workflow '{refId}' could not be deserialized.");
        }

        // An old version is held to today's rules: one published before a rule existed, or whose
        // integration or client has since gone, would otherwise go live and fail on every run.
        Result<IntegrationReferences> check = await CheckAsync(workspaceId, refId, definition, ct);
        if (check.IsFailure)
        {
            return Result<WorkflowStateResponse>.Failure(check.Error);
        }

        return await EnableCoreAsync(workspaceId, workspaceRef, row, definition, check.Value, ct);
    }

    private async Task<Result<WorkflowStateResponse>> EnableCoreAsync(int workspaceId, Guid workspaceRef, WorkflowDefinitionRow row, WorkflowDefinition definition, IntegrationReferences integrationRefs, CancellationToken ct)
    {
        TriggerPlan plan = _planner.Plan(definition, row.RefId, workspaceRef);

        WorkflowEnableResult result;
        try
        {
            result = await _states.EnableAsync(row.RefId, workspaceId, row.Version, CurrentUserId, plan, integrationRefs.Refs, ct);
        }
        catch (SqlException ex) when (WorkflowLifecycleErrors.TryMap(ex, row.RefId, row.Version, out Error error))
        {
            _logger.LogWarning("Enabling workflow '{RefId}' version {Version} refused: {Code}", row.RefId, row.Version, error.Code);
            return Result<WorkflowStateResponse>.Failure(error);
        }

        var response = new WorkflowStateResponse(row.RefId, WorkflowState.Enabled, row.Version);
        if (!result.Changed)
        {
            return Result<WorkflowStateResponse>.Success(response);
        }

        await AnnounceChangedAsync(workspaceId, row.RefId, [result.PreviousLiveDefinitionId, result.LiveDefinitionId], ct);

        int? previousVersion = result.PreviousLiveDefinitionId is int previousId ? (await _definitions.GetByIdAsync(previousId, ct)).Version : null;
        _logger.LogInformation("Workflow '{RefId}' version {Version} is live (was {PreviousVersion}); {Dropped} queued event(s) dropped", row.RefId, row.Version, previousVersion, result.DroppedQueuedEvents);
        await _eventBus.PublishAsync(new WorkflowEnabledEvent(row.RefId, row.Id, workspaceId, row.Version, previousVersion)
        {
            Changes = [new FieldChange("liveVersion", previousVersion?.ToString(CultureInfo.InvariantCulture), row.Version.ToString(CultureInfo.InvariantCulture))]
        }, ct);

        return Result<WorkflowStateResponse>.Success(response);
    }

    public async Task<Result<WorkflowStateResponse>> DisableAsync(int workspaceId, Guid refId, bool cancelRunningRuns, CancellationToken ct)
    {
        _logger.LogInformation("Disabling workflow '{RefId}' in WorkspaceId: {WorkspaceId} (cancel running runs: {Cancel})", refId, workspaceId, cancelRunningRuns);

        Result<WorkflowDisableResult> disabled = await TakeOutOfServiceAsync(workspaceId, refId, deprecate: false, ct);
        if (disabled.IsFailure)
        {
            return Result<WorkflowStateResponse>.Failure(disabled.Error);
        }

        WorkflowDisableResult result = disabled.Value;

        // Asked for, so honoured on a repeated disable too: runs going on a disabled workflow are what
        // the first call left behind. Queued, so a broker outage delays the cancels rather than failing
        // a disable that has already committed.
        int cancelled = 0;
        if (cancelRunningRuns)
        {
            foreach (long runId in result.ActiveRunIds)
            {
                await _engine.QueueCancelRunAsync(runId, DisabledCancellationReason, ct);
                cancelled++;
            }
        }

        if (result.Changed && result.PreviousLiveDefinitionId is int previousId)
        {
            await AnnounceChangedAsync(workspaceId, refId, [previousId], ct);
            WorkflowDefinitionRow previous = await _definitions.GetByIdAsync(previousId, ct);
            _logger.LogInformation("Workflow '{RefId}' disabled (was version {Version}): {Cancelled} run(s) cancelling, {Dropped} queued event(s) dropped", refId, previous.Version, cancelled, result.DroppedQueuedEvents);
            await _eventBus.PublishAsync(new WorkflowDisabledEvent(refId, previousId, workspaceId, previous.Version, cancelled, result.DroppedQueuedEvents)
            {
                Changes = [new FieldChange("state", nameof(WorkflowState.Enabled), nameof(WorkflowState.Disabled))]
            }, ct);
        }

        return Result<WorkflowStateResponse>.Success(new WorkflowStateResponse(refId, WorkflowState.Disabled, null, cancelled, result.DroppedQueuedEvents));
    }

    public async Task<Result<WorkflowStateResponse>> DeprecateAsync(int workspaceId, Guid refId, CancellationToken ct)
    {
        _logger.LogInformation("Deprecating workflow '{RefId}' in WorkspaceId: {WorkspaceId}", refId, workspaceId);

        // Read before the change only to name the state it came from in the audit entry.
        WorkflowStateRow? before = await _states.FindAsync(refId, workspaceId, ct);

        Result<WorkflowDisableResult> deprecated = await TakeOutOfServiceAsync(workspaceId, refId, deprecate: true, ct);
        if (deprecated.IsFailure)
        {
            return Result<WorkflowStateResponse>.Failure(deprecated.Error);
        }

        WorkflowDisableResult result = deprecated.Value;
        if (result.Changed)
        {
            if (result.PreviousLiveDefinitionId is int previousId)
            {
                await AnnounceChangedAsync(workspaceId, refId, [previousId], ct);
            }

            WorkflowDefinitionRow newest = await LoadNewestAsync(refId, ct);
            _logger.LogInformation("Workflow '{RefId}' deprecated; {Dropped} queued event(s) dropped", refId, result.DroppedQueuedEvents);
            await _eventBus.PublishAsync(new WorkflowDeprecatedEvent(refId, newest.Id, workspaceId, newest.Version)
            {
                Changes = [new FieldChange("state", (before?.State ?? WorkflowState.Disabled).ToString(), nameof(WorkflowState.Deprecated))]
            }, ct);
        }

        return Result<WorkflowStateResponse>.Success(new WorkflowStateResponse(refId, WorkflowState.Deprecated, null, 0, result.DroppedQueuedEvents));
    }

    public async Task<Result<WorkflowStateResponse>> ReinstateAsync(int workspaceId, Guid refId, CancellationToken ct)
    {
        _logger.LogInformation("Reinstating workflow '{RefId}' in WorkspaceId: {WorkspaceId}", refId, workspaceId);

        try
        {
            await _states.ReinstateAsync(refId, workspaceId, CurrentUserId, ct);
        }
        catch (SqlException ex) when (WorkflowLifecycleErrors.TryMap(ex, refId, null, out Error error))
        {
            return Result<WorkflowStateResponse>.Failure(error);
        }

        WorkflowDefinitionRow newest = await LoadNewestAsync(refId, ct);
        _logger.LogInformation("Workflow '{RefId}' reinstated as disabled", refId);
        await _eventBus.PublishAsync(new WorkflowReinstatedEvent(refId, newest.Id, workspaceId, newest.Version)
        {
            Changes = [new FieldChange("state", nameof(WorkflowState.Deprecated), nameof(WorkflowState.Disabled))]
        }, ct);

        return Result<WorkflowStateResponse>.Success(new WorkflowStateResponse(refId, WorkflowState.Disabled, null));
    }

    public async Task<Result> DeleteAsync(int workspaceId, Guid refId, CancellationToken ct)
    {
        _logger.LogInformation("Deleting workflow '{RefId}' in WorkspaceId: {WorkspaceId}", refId, workspaceId);

        var deletion = await _definitions.DeleteAsync(refId, workspaceId, CurrentUserId, ct);
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

        await AnnounceChangedAsync(workspaceId, refId, deletion.DefinitionIds.Select(id => (int?)id), ct);
        _logger.LogInformation("Workflow '{RefId}' deleted: {Versions} version(s), {Runs} run(s) cancelling", refId, deletion.DefinitionIds.Count, deletion.ActiveRunIds.Count);
        await _eventBus.PublishAsync(new WorkflowDeletedEvent(refId, deletion.DefinitionIds.DefaultIfEmpty().Max(), workspaceId, deletion.ActiveRunIds.Count), ct);
        return Result.Success();
    }

    private async Task<Result<WorkflowDisableResult>> TakeOutOfServiceAsync(int workspaceId, Guid refId, bool deprecate, CancellationToken ct)
    {
        try
        {
            return Result<WorkflowDisableResult>.Success(await _states.DisableAsync(refId, workspaceId, CurrentUserId, deprecate, ct));
        }
        catch (SqlException ex) when (WorkflowLifecycleErrors.TryMap(ex, refId, null, out Error error))
        {
            _logger.LogInformation("{Action} workflow '{RefId}' refused: {Code}", deprecate ? "Deprecating" : "Disabling", refId, error.Code);
            return Result<WorkflowDisableResult>.Failure(error);
        }
    }

    /// <summary>The newest version, which names a state change that has no live version in the audit log.</summary>
    private async Task<WorkflowDefinitionRow> LoadNewestAsync(Guid refId, CancellationToken ct)
    {
        return await _definitions.FindCurrentByRefIdAsync(refId, ct)
            ?? throw new InvalidOperationException($"Workflow '{refId}' changed state but has no version.");
    }

    /// <summary>
    /// What publishing and enabling both require of a definition: the validator passes, the integrations
    /// its steps name are this workspace's and of the right type, no client it names is gone (one
    /// only revoked is a warning, as it can be registered again), and every workflow a step runs is
    /// this workspace's.
    /// </summary>
    private async Task<Result<IntegrationReferences>> CheckAsync(int workspaceId, Guid refId, WorkflowDefinition? definition, CancellationToken ct)
    {
        // The request's id is the one the version is stored and triggered under, which the definition's
        // own WorkflowRefId is not required to match, so the validator is told it for the self-reference check.
        ValidationResult validation = _validator.Validate(definition, refId);
        if (!validation.IsValid)
        {
            return Result<IntegrationReferences>.Failure(ValidationFailed(validation.Issues));
        }

        IntegrationReferences integrationRefs = await _integrationCheck.CheckAsync(workspaceId, definition, ct);
        if (integrationRefs.Issues.Count > 0)
        {
            return Result<IntegrationReferences>.Failure(ValidationFailed(integrationRefs.Issues));
        }

        if (_clientCheck is not null)
        {
            var clientIssues = await _clientCheck.CheckAsync(workspaceId, definition, ct);
            if (clientIssues.Any(issue => issue.Severity == ValidationSeverity.Error))
            {
                return Result<IntegrationReferences>.Failure(ValidationFailed(clientIssues));
            }
        }

        if (_subWorkflowCheck is not null)
        {
            var subWorkflowIssues = await _subWorkflowCheck.CheckAsync(workspaceId, definition, ct);
            if (subWorkflowIssues.Any(issue => issue.Severity == ValidationSeverity.Error))
            {
                return Result<IntegrationReferences>.Failure(ValidationFailed(subWorkflowIssues));
            }
        }

        return Result<IntegrationReferences>.Success(integrationRefs);
    }

    /// <summary>
    /// Each error names its node, so the message points at what to fix rather than being a bare
    /// sentence. The structured form is available from the validate endpoint.
    /// </summary>
    private Error ValidationFailed(IEnumerable<ValidationIssue> issues)
    {
        string message = string.Join("; ", issues
            .Where(issue => issue.Severity == ValidationSeverity.Error)
            .Select(issue => (issue.NodeId, issue.Field) switch
            {
                (Guid nodeId, string field) => $"[{issue.Code}] {issue.Message} (node {nodeId}, {field})",
                (Guid nodeId, null) => $"[{issue.Code}] {issue.Message} (node {nodeId})",
                _ => $"[{issue.Code}] {issue.Message}"
            }));
        _logger.LogWarning("Workflow validation failed: {Issues}", message);
        return Error.Validation("WORKFLOW_VALIDATION_FAILED", message);
    }

    /// <summary>
    /// Tells the engine that these versions' definitions or trigger registrations changed, so it drops
    /// what it cached of them (<see cref="WorkflowDefinitionChangedEvent"/>).
    /// </summary>
    private Task AnnounceChangedAsync(int workspaceId, Guid refId, IEnumerable<int?> definitionIds, CancellationToken ct)
    {
        int[] ids = definitionIds.OfType<int>().Distinct().ToArray();
        return _eventBus.PublishAsync(new WorkflowDefinitionChangedEvent(refId, workspaceId, ids), ct);
    }

    /// <summary>
    /// A publish names its own RefId, so it can land on one another workspace already uses. That is
    /// a conflict to resolve by choosing another RefId, not a permission the caller lacks: inside a
    /// workspace a 403 only ever means a missing permission (see "The ID Boundary").
    /// </summary>
    private static readonly Error WorkflowRefTaken = Error.Conflict("WORKFLOW_REF_TAKEN", "This workflow reference is already in use. Publish under a new RefId.");
}
