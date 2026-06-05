using System.Text.Json;
using Wbskt.Management.Models.Workflow;
using Wbskt.Primitives.Exceptions;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Abstraction.Validation;
using Wbskt.Workflow.Abstraction.Enums;

namespace Wbskt.Management.Host.Services;

public sealed class WorkflowDefinitionService : IWorkflowDefinitionService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IWorkflowDefinitionProvider _workflowDefinitionProvider;
    private readonly ITriggerRegistrationService _triggerRegistrationService;
    private readonly IWorkflowDefinitionCache _cache;
    private readonly WorkflowValidator _validator;

    public WorkflowDefinitionService(
        IWorkflowDefinitionProvider workflowDefinitionProvider,
        ITriggerRegistrationService triggerRegistrationService,
        IWorkflowDefinitionCache cache,
        WorkflowValidator validator)
    {
        _workflowDefinitionProvider = workflowDefinitionProvider;
        _triggerRegistrationService = triggerRegistrationService;
        _cache = cache;
        _validator = validator;
    }

    public async Task<WorkflowPublishResponse> PublishAsync(WorkflowPublishRequest request, CancellationToken ct)
    {
        WorkflowDefinition definition = JsonSerializer.Deserialize<WorkflowDefinition>(request.Definition.GetRawText(), SerializerOptions)
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
        WorkflowDefinitionRow inserted = await _workflowDefinitionProvider.InsertAsync(new WorkflowDefinitionRow
        {
            Id = 0,
            RefId = request.RefId,
            Version = nextVersion,
            WorkspaceId = definition.WorkspaceId,
            Name = request.Name,
            Description = request.Description,
            IsEnabled = true,
            DefinitionJson = request.Definition.GetRawText(),
            PublishedBy = definition.PublishedBy,
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

    public async Task<WorkflowDefinitionDto> GetCurrentAsync(Guid refId, CancellationToken ct)
    {
        WorkflowDefinitionRow row = await _workflowDefinitionProvider.GetCurrentByRefIdAsync(refId, ct);
        return Map(row);
    }

    public async Task<WorkflowDefinitionDto> GetVersionAsync(Guid refId, int version, CancellationToken ct)
    {
        WorkflowDefinitionRow row = await _workflowDefinitionProvider.GetByRefIdVersionAsync(refId, version, ct);
        return Map(row);
    }

    public async Task DeprecateAsync(Guid refId, CancellationToken ct)
    {
        WorkflowDefinitionRow row = await _workflowDefinitionProvider.GetCurrentByRefIdAsync(refId, ct);
        await _workflowDefinitionProvider.DeprecateAsync(row.Id, ct);
        await _triggerRegistrationService.OnDeprecatedAsync(row.Id, ct);
        _cache.Invalidate(row.Id);
    }

    private static WorkflowDefinitionDto Map(WorkflowDefinitionRow row)
    {
        JsonElement definition = JsonSerializer.Deserialize<JsonElement>(row.DefinitionJson, SerializerOptions);
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
