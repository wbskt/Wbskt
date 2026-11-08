using System.Text.Json;
using Microsoft.Data.SqlClient;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Security;
using Wbskt.Management.Models.Workflow;
using Wbskt.Models;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Providers;

namespace Wbskt.Management.Host.Services.Workflow;

public sealed class WorkflowDraftService : IWorkflowDraftService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private const int WorkflowOwnedElsewhereError = 50021; // THROW in dbo.WorkflowDraft_Save
    private const int WorkflowDeletedError = 50022; // THROW in dbo.WorkflowDraft_Save
    private const int DraftRevisionMovedError = 50031; // THROW in dbo.WorkflowDraft_Save
    private const int DraftLimitReachedError = 50032; // THROW in dbo.WorkflowDraft_Save
    private const int MaxNameLength = 200;
    private const int MaxDescriptionLength = 2000;

    public const int DefaultMaxNewDraftsPerWorkspace = 50;

    private readonly IWorkflowDraftProvider _drafts;
    private readonly IWorkflowQueryService _queryService;
    private readonly IWorkflowLifecycleService _lifecycleService;
    private readonly IIdentityService _identityService;
    private readonly ILogger<WorkflowDraftService> _logger;
    private readonly IConfiguration? _configuration;

    public WorkflowDraftService(
        IWorkflowDraftProvider drafts,
        IWorkflowQueryService queryService,
        IWorkflowLifecycleService lifecycleService,
        IIdentityService identityService,
        ILogger<WorkflowDraftService> logger,
        IConfiguration? configuration = null)
    {
        _drafts = drafts;
        _queryService = queryService;
        _lifecycleService = lifecycleService;
        _identityService = identityService;
        _logger = logger;
        _configuration = configuration;
    }

    // Drafts of workflows never published hold no triggers, so they don't count against the workflow
    // ceiling; they have this one of their own.
    private int MaxNewDraftsPerWorkspace =>
        _configuration?.GetValue("Workflows:MaxNewDraftsPerWorkspace", DefaultMaxNewDraftsPerWorkspace) ?? DefaultMaxNewDraftsPerWorkspace;

    public async Task<IReadOnlyList<WorkflowDraftSummaryDto>> ListAsync(int workspaceId, CancellationToken ct)
    {
        var rows = await _drafts.ListAsync(workspaceId, ct);
        return rows.Select(row => new WorkflowDraftSummaryDto(
            row.RefId,
            row.Revision,
            row.BaseVersion,
            row.CurrentVersion,
            IsOutdated(row),
            row.Name,
            row.Description,
            row.UpdatedByRefId,
            row.UpdatedAt.ToUtcOffset())).ToList();
    }

    public async Task<Result<WorkflowDraftDto>> GetAsync(int workspaceId, Guid refId, CancellationToken ct)
    {
        WorkflowDraftRow? row = await _drafts.FindAsync(refId, workspaceId, ct);
        return row is null
            ? Result<WorkflowDraftDto>.Failure(WorkflowDraftErrors.NotFound)
            : Result<WorkflowDraftDto>.Success(await MapAsync(row, ct));
    }

    public async Task<Result<WorkflowDraftDto>> SaveAsync(int workspaceId, Guid refId, WorkflowDraftSaveRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > MaxNameLength)
        {
            return Result<WorkflowDraftDto>.Failure(Error.Validation("WORKFLOW_DRAFT_INVALID", $"A draft needs a name of at most {MaxNameLength} characters."));
        }

        if (request.Description?.Length > MaxDescriptionLength)
        {
            return Result<WorkflowDraftDto>.Failure(Error.Validation("WORKFLOW_DRAFT_INVALID", $"A description can be at most {MaxDescriptionLength} characters."));
        }

        UserIdentity user = _identityService.GetUserIdentity();
        var save = new WorkflowDraftSave(
            refId,
            workspaceId,
            request.Name,
            request.Description,
            JsonSerializer.Serialize(request.Definition, SerializerOptions),
            user.UserId,
            user.UserRefId);

        WorkflowDraftRow row;
        try
        {
            row = await _drafts.SaveAsync(save, request.ExpectedRevision, MaxNewDraftsPerWorkspace, ct);
        }
        catch (SqlException ex) when (ex.Number == DraftRevisionMovedError)
        {
            _logger.LogInformation("Draft save refused: the draft of workflow '{RefId}' is no longer at revision {Revision}", refId, request.ExpectedRevision);
            return Result<WorkflowDraftDto>.Failure(WorkflowDraftErrors.Conflict);
        }
        catch (SqlException ex) when (ex.Number == DraftLimitReachedError)
        {
            return Result<WorkflowDraftDto>.Failure(Error.Conflict(
                "WORKFLOW_DRAFT_LIMIT_REACHED",
                $"A workspace can have at most {MaxNewDraftsPerWorkspace} drafts of workflows not yet published. Publish or discard one."));
        }
        catch (SqlException ex) when (ex.Number == WorkflowOwnedElsewhereError)
        {
            _logger.LogWarning("Draft save rejected: Workflow '{RefId}' belongs to another workspace than {WorkspaceId}", refId, workspaceId);
            return Result<WorkflowDraftDto>.Failure(WorkflowDraftErrors.RefTaken);
        }
        catch (SqlException ex) when (ex.Number == WorkflowDeletedError)
        {
            return Result<WorkflowDraftDto>.Failure(WorkflowDraftErrors.Deleted);
        }

        return Result<WorkflowDraftDto>.Success(await MapAsync(row, ct));
    }

    public async Task<Result> DiscardAsync(int workspaceId, Guid refId, int? revision, CancellationToken ct)
    {
        if (await _drafts.DeleteAsync(refId, workspaceId, revision, ct))
        {
            return Result.Success();
        }

        // Nothing was removed: either there is no draft, or it was saved again since the caller read it.
        return await _drafts.FindAsync(refId, workspaceId, ct) is null
            ? Result.Failure(WorkflowDraftErrors.NotFound)
            : Result.Failure(WorkflowDraftErrors.Conflict);
    }

    public async Task<Result<WorkflowPublishResponse>> PublishAsync(int workspaceId, Guid workspaceRef, Guid refId, WorkflowDraftPublishRequest request, CancellationToken ct)
    {
        WorkflowDraftRow? row = await _drafts.FindAsync(refId, workspaceId, ct);
        if (row is null)
        {
            return Result<WorkflowPublishResponse>.Failure(WorkflowDraftErrors.NotFound);
        }

        if (row.Revision != request.Revision)
        {
            return Result<WorkflowPublishResponse>.Failure(WorkflowDraftErrors.Conflict);
        }

        if (!request.Force && IsOutdated(row))
        {
            return Result<WorkflowPublishResponse>.Failure(WorkflowDraftErrors.Outdated);
        }

        // The version check is made again under the publish's lock, since a version can land after the
        // read above; the draft is deleted in the same transaction, at this revision only.
        var guards = new WorkflowPublishGuards(
            ExpectedCurrentVersion: request.Force ? null : row.BaseVersion ?? 0,
            DraftRevision: row.Revision);

        return await _lifecycleService.PublishAsync(
            workspaceId,
            workspaceRef,
            new WorkflowPublishRequest(refId, row.Name, row.Description, Deserialize(row)),
            guards,
            ct);
    }

    private static bool IsOutdated(WorkflowDraftRow row) => row.CurrentVersion != row.BaseVersion;

    private async Task<WorkflowDraftDto> MapAsync(WorkflowDraftRow row, CancellationToken ct)
    {
        WorkflowDefinition definition = Deserialize(row);
        WorkflowValidationResponse validation = await _queryService.ValidateAsync(row.WorkspaceId, definition, ct);

        return new WorkflowDraftDto(
            row.RefId,
            row.Revision,
            row.BaseVersion,
            row.CurrentVersion,
            IsOutdated(row),
            row.Name,
            row.Description,
            definition,
            row.UpdatedByRefId,
            row.UpdatedAt.ToUtcOffset(),
            row.CreatedAt.ToUtcOffset(),
            validation);
    }

    private static WorkflowDefinition Deserialize(WorkflowDraftRow row) =>
        JsonSerializer.Deserialize<WorkflowDefinition>(row.DefinitionJson, SerializerOptions)
            ?? throw new InvalidOperationException($"Could not deserialize the draft of workflow '{row.RefId}'.");
}
