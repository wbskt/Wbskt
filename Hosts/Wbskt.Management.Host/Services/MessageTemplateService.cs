using Wbskt.Protocol;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events;
using Wbskt.Events.Abstractions;
using Wbskt.Events.Templates;
using Wbskt.Infrastructure;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Models;
using Wbskt.Management.Host.Providers;
using Wbskt.Models;

namespace Wbskt.Management.Host.Services;

internal sealed class MessageTemplateService : IMessageTemplateService
{
    public const int DefaultMaxPerWorkspace = 200;

    // A change event keeps a payload's first characters only, as a shared variable's does.
    private const int ChangeValueLength = 256;

    private const int LimitReachedError = 50045; // THROW in dbo.MessageTemplate_Create
    private const int UniqueViolation = 2627;
    private const int UniqueIndexViolation = 2601;

    private static readonly Error NameTaken = Error.Conflict("TEMPLATE_NAME_TAKEN", "This workspace already has a template with that name.");

    private readonly IMessageTemplateProvider _templateProvider;
    private readonly IRegistrationPolicyProvider _policyProvider;
    private readonly IEventBus _eventBus;
    private readonly IConfiguration _configuration;
    private readonly ILogger<MessageTemplateService> _logger;

    public MessageTemplateService(
        IMessageTemplateProvider templateProvider,
        IRegistrationPolicyProvider policyProvider,
        IEventBus eventBus,
        IConfiguration configuration,
        ILogger<MessageTemplateService> logger)
    {
        _templateProvider = templateProvider;
        _policyProvider = policyProvider;
        _eventBus = eventBus;
        _configuration = configuration;
        _logger = logger;
    }

    private int MaxPerWorkspace => _configuration.GetValue("Templates:MaxPerWorkspace", DefaultMaxPerWorkspace);

    public async Task<Result<IPagedList<MessageTemplateResponse>>> GetAllAsync(int workspaceId, Guid? policyRefId, int skip, int take,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Querying message templates for WorkspaceId: {WorkspaceId}", workspaceId);

        int? policyId = null;
        if (policyRefId.HasValue)
        {
            // Another workspace's policy is as unknown here as one that does not exist, not an empty page.
            var policy = await WorkspaceOwnership.LoadAsync(workspaceId, () => _policyProvider.FindByRefIdAsync(policyRefId.Value, cancellationToken), WorkspaceOwnership.PolicyNotFound);
            if (policy.IsFailure)
            {
                return Result<IPagedList<MessageTemplateResponse>>.Failure(policy.Error);
            }

            policyId = policy.Value.Id;
        }

        var templates = await _templateProvider.GetAllAsync(workspaceId, policyId, skip, take, cancellationToken);
        var result = new PagedList<MessageTemplateResponse>(templates.Select(MapToResponse), templates.TotalCount);
        return Result<IPagedList<MessageTemplateResponse>>.Success(result);
    }

    public async Task<Result<MessageTemplateResponse>> CreateAsync(int workspaceId, MessageTemplateRequest request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Creating message template '{TemplateName}' in WorkspaceId: {WorkspaceId}", request.Name, workspaceId);

        var validation = await ValidateAsync(workspaceId, request, cancellationToken);
        if (validation.Result.IsFailure)
        {
            return Result<MessageTemplateResponse>.Failure(validation.Result.Error);
        }

        MessageTemplate template;
        try
        {
            template = await _templateProvider.InsertAsync(workspaceId, validation.PolicyId, request.Name.Trim(),
                request.MessageType.Trim(), request.PayloadJson, MaxPerWorkspace, cancellationToken);
        }
        catch (SqlException ex) when (ex.Number == LimitReachedError)
        {
            return Result<MessageTemplateResponse>.Failure(Error.Conflict(
                "TEMPLATE_LIMIT_REACHED",
                $"A workspace can have at most {MaxPerWorkspace} message templates. Delete one you no longer use."));
        }
        catch (SqlException ex) when (ex.Number is UniqueViolation or UniqueIndexViolation)
        {
            return Result<MessageTemplateResponse>.Failure(NameTaken);
        }

        await _eventBus.PublishAsync(new MessageTemplateCreatedEvent(template.RefId, workspaceId, template.Name, template.MessageType), cancellationToken);
        return Result<MessageTemplateResponse>.Success(MapToResponse(template));
    }

    public async Task<Result<MessageTemplateResponse>> UpdateAsync(int workspaceId, Guid refId, MessageTemplateRequest request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Updating message template '{RefId}' in WorkspaceId: {WorkspaceId}", refId, workspaceId);

        var validation = await ValidateAsync(workspaceId, request, cancellationToken);
        if (validation.Result.IsFailure)
        {
            return Result<MessageTemplateResponse>.Failure(validation.Result.Error);
        }

        var existing = await FindOwnedTemplateAsync(workspaceId, refId, cancellationToken);
        if (existing.IsFailure)
        {
            return Result<MessageTemplateResponse>.Failure(existing.Error);
        }

        var before = existing.Value;
        try
        {
            await _templateProvider.UpdateAsync(workspaceId, before.Id, validation.PolicyId, request.Name.Trim(),
                request.MessageType.Trim(), request.PayloadJson, cancellationToken);
        }
        catch (SqlException ex) when (ex.Number is UniqueViolation or UniqueIndexViolation)
        {
            return Result<MessageTemplateResponse>.Failure(NameTaken);
        }

        var after = await _templateProvider.FindByRefIdAsync(refId, cancellationToken);
        if (after is null)
        {
            // Deleted between the write and this read.
            return Result<MessageTemplateResponse>.Failure(WorkspaceOwnership.TemplateNotFound);
        }

        var changes = Changes(before, after);
        if (changes.Count > 0)
        {
            await _eventBus.PublishAsync(new MessageTemplateUpdatedEvent(refId, workspaceId, after.Name, after.MessageType) { Changes = changes }, cancellationToken);
        }

        return Result<MessageTemplateResponse>.Success(MapToResponse(after));
    }

    public async Task<Result> DeleteAsync(int workspaceId, Guid refId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Deleting message template '{RefId}' in WorkspaceId: {WorkspaceId}", refId, workspaceId);

        var existing = await FindOwnedTemplateAsync(workspaceId, refId, cancellationToken);
        if (existing.IsFailure)
        {
            return Result.Failure(existing.Error);
        }

        await _templateProvider.DeleteAsync(workspaceId, existing.Value.Id, cancellationToken);
        await _eventBus.PublishAsync(new MessageTemplateDeletedEvent(refId, workspaceId, existing.Value.Name, existing.Value.MessageType), cancellationToken);
        return Result.Success();
    }

    private async Task<Result<MessageTemplate>> FindOwnedTemplateAsync(int workspaceId, Guid refId, CancellationToken cancellationToken)
    {
        var lookup = await WorkspaceOwnership.LoadAsync(workspaceId, () => _templateProvider.FindByRefIdAsync(refId, cancellationToken), WorkspaceOwnership.TemplateNotFound);
        if (lookup.IsFailure)
        {
            _logger.LogWarning("Message template '{RefId}' not found in WorkspaceId: {WorkspaceId}", refId, workspaceId);
        }

        return lookup;
    }

    private async Task<(Result Result, int? PolicyId)> ValidateAsync(int workspaceId, MessageTemplateRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 100)
        {
            return (Result.Failure(Error.Validation("TEMPLATE_NAME_INVALID", "Template name must be 1-100 characters.")), null);
        }

        var messageType = request.MessageType?.Trim();
        if (!ClientMessageRules.IsValidType(messageType))
        {
            return (Result.Failure(Error.Validation("TEMPLATE_TYPE_INVALID", "Message type must be 1-100 characters.")), null);
        }

        if (ClientMessageRules.IsReserved(messageType))
        {
            return (Result.Failure(Error.Validation("TEMPLATE_TYPE_RESERVED", "Message type is reserved for the platform protocol.")), null);
        }

        if (string.IsNullOrWhiteSpace(request.PayloadJson))
        {
            return (Result.Failure(Error.Validation("TEMPLATE_PAYLOAD_INVALID", $"Payload must be 1-{ClientMessageRules.MaxPayloadChars} characters.")), null);
        }

        // The rule a send applies, so a template saved here can always be sent.
        if (ClientMessageRules.Check(messageType, request.PayloadJson) is { } violation)
        {
            return (Result.Failure(Error.Validation("TEMPLATE_PAYLOAD_INVALID", violation.Message)), null);
        }

        try
        {
            using var _ = JsonDocument.Parse(request.PayloadJson);
        }
        catch (JsonException)
        {
            return (Result.Failure(Error.Validation("TEMPLATE_PAYLOAD_INVALID", "Payload must be valid JSON.")), null);
        }

        int? policyId = null;
        if (request.PolicyRefId.HasValue)
        {
            var policy = await WorkspaceOwnership.LoadAsync(workspaceId, () => _policyProvider.FindByRefIdAsync(request.PolicyRefId.Value, cancellationToken), WorkspaceOwnership.PolicyNotFound);
            if (policy.IsFailure)
            {
                return (Result.Failure(policy.Error), null);
            }

            policyId = policy.Value.Id;
        }

        return (Result.Success(), policyId);
    }

    private static List<FieldChange> Changes(MessageTemplate before, MessageTemplate after)
    {
        var changes = new List<FieldChange>();
        if (before.Name != after.Name)
        {
            changes.Add(new FieldChange("name", before.Name, after.Name));
        }

        if (before.MessageType != after.MessageType)
        {
            changes.Add(new FieldChange("messageType", before.MessageType, after.MessageType));
        }

        if (before.PolicyRefId != after.PolicyRefId)
        {
            changes.Add(new FieldChange("policyRefId", before.PolicyRefId?.ToString(), after.PolicyRefId?.ToString()));
        }

        if (before.PayloadJson != after.PayloadJson)
        {
            changes.Add(new FieldChange("payloadJson", Cut(before.PayloadJson), Cut(after.PayloadJson)));
        }

        return changes;
    }

    private static string Cut(string value) => value.Length > ChangeValueLength ? value[..ChangeValueLength] : value;

    private static MessageTemplateResponse MapToResponse(MessageTemplate t)
    {
        return new MessageTemplateResponse(
            t.RefId,
            t.Name,
            t.MessageType,
            t.PayloadJson,
            t.PolicyRefId,
            t.CreatedAt.ToUtcOffset(),
            t.UpdatedAt.ToUtcOffset()
        );
    }
}
