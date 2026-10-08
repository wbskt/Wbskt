using System.Text.Json;
using Wbskt.Infrastructure;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Providers;
using Wbskt.Models;

namespace Wbskt.Management.Host.Services;

internal sealed class MessageTemplateService : IMessageTemplateService
{
    private const int MaxPayloadChars = 32 * 1024; // half the 64 KB socket message cap

    private readonly IMessageTemplateProvider _templateProvider;
    private readonly IRegistrationPolicyProvider _policyProvider;
    private readonly ILogger<MessageTemplateService> _logger;

    public MessageTemplateService(
        IMessageTemplateProvider templateProvider,
        IRegistrationPolicyProvider policyProvider,
        ILogger<MessageTemplateService> logger)
    {
        _templateProvider = templateProvider;
        _policyProvider = policyProvider;
        _logger = logger;
    }

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

        var template = await _templateProvider.InsertAsync(workspaceId, validation.PolicyId, request.Name.Trim(),
            request.MessageType.Trim(), request.PayloadJson, cancellationToken);
        return Result<MessageTemplateResponse>.Success(MapToResponse(template));
    }

    public async Task<Result> UpdateAsync(int workspaceId, Guid refId, MessageTemplateRequest request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Updating message template '{RefId}' in WorkspaceId: {WorkspaceId}", refId, workspaceId);

        var validation = await ValidateAsync(workspaceId, request, cancellationToken);
        if (validation.Result.IsFailure)
        {
            return validation.Result;
        }

        var existing = await FindOwnedTemplateAsync(workspaceId, refId, cancellationToken);
        if (existing.IsFailure)
        {
            return Result.Failure(existing.Error);
        }

        await _templateProvider.UpdateAsync(workspaceId, existing.Value.Id, validation.PolicyId, request.Name.Trim(),
            request.MessageType.Trim(), request.PayloadJson, cancellationToken);
        return Result.Success();
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
        if (string.IsNullOrEmpty(messageType) || messageType.Length > 100)
        {
            return (Result.Failure(Error.Validation("TEMPLATE_TYPE_INVALID", "Message type must be 1-100 characters.")), null);
        }

        if (ReservedMessageTypes.IsReserved(messageType))
        {
            return (Result.Failure(Error.Validation("TEMPLATE_TYPE_RESERVED", "Message type is reserved for the platform protocol.")), null);
        }

        if (string.IsNullOrWhiteSpace(request.PayloadJson) || request.PayloadJson.Length > MaxPayloadChars)
        {
            return (Result.Failure(Error.Validation("TEMPLATE_PAYLOAD_INVALID", $"Payload must be 1-{MaxPayloadChars} characters.")), null);
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

    private static MessageTemplateResponse MapToResponse(MessageTemplate t)
    {
        return new MessageTemplateResponse(
            t.RefId,
            t.Name,
            t.MessageType,
            t.PayloadJson,
            t.PolicyRefId,
            t.CreatedAt,
            t.UpdatedAt
        );
    }
}
