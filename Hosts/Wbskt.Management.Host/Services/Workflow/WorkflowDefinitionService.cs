using System.Text.Json;
using Wbskt.Infrastructure.Security;
using Wbskt.Management.Models.Workflow;
using Wbskt.Primitives.Exceptions;
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

    public WorkflowDefinitionService(
        IWorkflowDefinitionProvider workflowDefinitionProvider,
        ITriggerRegistrationService triggerRegistrationService,
        IWorkflowDefinitionCache cache,
        WorkflowValidator validator,
        IIdentityService identityService)
    {
        _workflowDefinitionProvider = workflowDefinitionProvider;
        _triggerRegistrationService = triggerRegistrationService;
        _cache = cache;
        _validator = validator;
        _identityService = identityService;
    }

    public async Task<WorkflowPublishResponse> PublishAsync(int workspaceId, WorkflowPublishRequest request, CancellationToken ct)
    {
        WorkflowDefinition definition = request.Definition
            ?? throw new ValidationException("Workflow definition could not be deserialized.");
        ValidationResult validation = _validator.Validate(definition);
        if (!validation.IsValid)
        {
            string message = string.Join("; ", validation.Issues
                .Where(issue => issue.Severity == ValidationSeverity.Error)
                .Select(issue => issue.Message));
            throw new ValidationException(message);
        }

        WorkflowDefinitionRow? existing;
        try
        {
            existing = await _workflowDefinitionProvider.GetCurrentByRefIdAsync(request.RefId, ct);
        }
        catch (NotFoundException)
        {
            existing = null;
        }
        catch (KeyNotFoundException)
        {
            existing = null;
        }

        int nextVersion = existing?.Version + 1 ?? 1;

        // A workflow RefId is owned by the workspace that first published it; a different
        // workspace cannot publish a new version over it.
        if (existing is not null && existing.WorkspaceId != workspaceId)
        {
            throw new SecurityException($"Workflow '{request.RefId}' does not belong to the workspace.");
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
            CreatedAt = definition.CreatedAt
        }, ct);

        if (existing is not null)
        {
            await _workflowDefinitionProvider.DeprecateAsync(existing.Id, ct);
            await _triggerRegistrationService.OnDeprecatedAsync(existing.Id, ct);
            _cache.Invalidate(existing.Id);
        }

        await _triggerRegistrationService.OnPublishedAsync(inserted.Id, ct);
        _cache.Invalidate(inserted.Id);
        return new WorkflowPublishResponse(inserted.RefId, inserted.Version, "Published");
    }

    public async Task<WorkflowDefinitionDto> GetCurrentAsync(int workspaceId, Guid refId, CancellationToken ct)
    {
        WorkflowDefinitionRow row = await _workflowDefinitionProvider.GetCurrentByRefIdAsync(refId, ct);
        EnsureWorkspace(row, workspaceId, refId);
        return Map(row);
    }

    public async Task<WorkflowDefinitionDto> GetVersionAsync(int workspaceId, Guid refId, int version, CancellationToken ct)
    {
        WorkflowDefinitionRow row = await _workflowDefinitionProvider.GetByRefIdVersionAsync(refId, version, ct);
        EnsureWorkspace(row, workspaceId, refId);
        return Map(row);
    }

    public async Task DeprecateAsync(int workspaceId, Guid refId, CancellationToken ct)
    {
        WorkflowDefinitionRow row = await _workflowDefinitionProvider.GetCurrentByRefIdAsync(refId, ct);
        EnsureWorkspace(row, workspaceId, refId);
        await _workflowDefinitionProvider.DeprecateAsync(row.Id, ct);
        await _triggerRegistrationService.OnDeprecatedAsync(row.Id, ct);
        _cache.Invalidate(row.Id);
    }

    public async Task<Wbskt.Models.IPagedList<WorkflowSummaryDto>> GetAllSummariesAsync(int workspaceId, int skip, int take, CancellationToken ct)
    {
        var result = await _workflowDefinitionProvider.GetAllSummariesAsync(workspaceId, skip, take, ct);

        var dtos = result.Select(row => new WorkflowSummaryDto(
            row.RefId,
            row.Version,
            row.IsEnabled ? "Published" : "Deprecated",
            row.Name,
            row.Description,
            row.CreatedAt
        )).ToList();

        return new Wbskt.Models.PagedList<WorkflowSummaryDto>(dtos, result.TotalCount);
    }

    public async Task EnsureWorkflowInWorkspaceAsync(int workspaceId, Guid workflowRefId, CancellationToken ct)
    {
        WorkflowDefinitionRow row = await _workflowDefinitionProvider.GetCurrentByRefIdAsync(workflowRefId, ct);
        EnsureWorkspace(row, workspaceId, workflowRefId);
    }

    private static void EnsureWorkspace(WorkflowDefinitionRow row, int workspaceId, Guid refId)
    {
        // Failed ownership is reported as a security error (403), never NotFound, to avoid
        // letting callers enumerate workflows in other workspaces.
        if (row.WorkspaceId != workspaceId)
        {
            throw new SecurityException($"Workflow '{refId}' does not belong to the workspace.");
        }
    }

    private static WorkflowDefinitionDto Map(WorkflowDefinitionRow row)
    {
        WorkflowDefinition definition = JsonSerializer.Deserialize<WorkflowDefinition>(row.DefinitionJson, SerializerOptions)
            ?? throw new InvalidOperationException($"Could not deserialize workflow definition for '{row.RefId}'");
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
