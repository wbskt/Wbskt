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

        try
        {
            int? policyId = null;
            if (policyRefId.HasValue)
            {
                policyId = await _policyProvider.FindIdByRefIdAsync(policyRefId.Value, cancellationToken);
                if (policyId <= 0)
                {
                    return Result<IPagedList<MessageTemplateResponse>>.Failure(Error.NotFound("POLICY_NOT_FOUND", "Registration policy not found."));
                }
            }

            var templates = await _templateProvider.GetAllAsync(workspaceId, policyId, skip, take, cancellationToken);
            var result = new PagedList<MessageTemplateResponse>(templates.Select(MapToResponse), templates.TotalCount);
            return Result<IPagedList<MessageTemplateResponse>>.Success(result);
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to query message templates for WorkspaceId: {WorkspaceId}. Error: {Message}", workspaceId, ex.Message);
            _logger.LogTrace(ex, "GetAllAsync exception stack trace for WorkspaceId {WorkspaceId}", workspaceId);
            return Result<IPagedList<MessageTemplateResponse>>.Failure(Error.Failure("TEMPLATE_QUERY_ERROR", ex.Message));
        }
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

        try
        {
            var template = await _templateProvider.InsertAsync(workspaceId, validation.PolicyId, request.Name.Trim(),
                request.MessageType.Trim(), request.PayloadJson, cancellationToken);
            return Result<MessageTemplateResponse>.Success(MapToResponse(template));
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to create message template '{TemplateName}'. Error: {Message}", request.Name, ex.Message);
            _logger.LogTrace(ex, "CreateAsync exception stack trace for '{TemplateName}'", request.Name);
            return Result<MessageTemplateResponse>.Failure(Error.Failure("TEMPLATE_CREATE_ERROR", ex.Message));
        }
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

        try
        {
            var existing = await FindOwnedTemplateAsync(workspaceId, refId, cancellationToken);
            if (existing.IsFailure)
            {
                return Result.Failure(existing.Error);
            }

            await _templateProvider.UpdateAsync(workspaceId, existing.Value.Id, validation.PolicyId, request.Name.Trim(),
                request.MessageType.Trim(), request.PayloadJson, cancellationToken);
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to update message template '{RefId}'. Error: {Message}", refId, ex.Message);
            _logger.LogTrace(ex, "UpdateAsync exception stack trace for '{RefId}'", refId);
            return Result.Failure(Error.Failure("TEMPLATE_UPDATE_ERROR", ex.Message));
        }
    }

    public async Task<Result> DeleteAsync(int workspaceId, Guid refId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Deleting message template '{RefId}' in WorkspaceId: {WorkspaceId}", refId, workspaceId);

        try
        {
            var existing = await FindOwnedTemplateAsync(workspaceId, refId, cancellationToken);
            if (existing.IsFailure)
            {
                return Result.Failure(existing.Error);
            }

            await _templateProvider.DeleteAsync(workspaceId, existing.Value.Id, cancellationToken);
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to delete message template '{RefId}'. Error: {Message}", refId, ex.Message);
            _logger.LogTrace(ex, "DeleteAsync exception stack trace for '{RefId}'", refId);
            return Result.Failure(Error.Failure("TEMPLATE_DELETE_ERROR", ex.Message));
        }
    }

    private async Task<Result<MessageTemplate>> FindOwnedTemplateAsync(int workspaceId, Guid refId, CancellationToken cancellationToken)
    {
        MessageTemplate template;
        try
        {
            template = await _templateProvider.GetByRefIdAsync(refId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Message template '{RefId}' not found. Error: {Message}", refId, ex.Message);
            return Result<MessageTemplate>.Failure(Error.NotFound("TEMPLATE_NOT_FOUND", "Message template not found."));
        }

        if (template.WorkspaceId != workspaceId)
        {
            _logger.LogWarning("Template access rejected: '{RefId}' does not belong to WorkspaceId: {WorkspaceId}", refId, workspaceId);
            return Result<MessageTemplate>.Failure(Error.Unauthorized("TEMPLATE_UNAUTHORIZED", "Template does not belong to this workspace."));
        }

        return Result<MessageTemplate>.Success(template);
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
            RegistrationPolicy policy;
            try
            {
                policy = await _policyProvider.GetByRefIdAsync(request.PolicyRefId.Value, cancellationToken);
            }
            catch (Exception)
            {
                return (Result.Failure(Error.NotFound("POLICY_NOT_FOUND", "Registration policy not found.")), null);
            }

            if (policy.WorkspaceId != workspaceId)
            {
                return (Result.Failure(Error.Unauthorized("POLICY_UNAUTHORIZED", "Policy does not belong to this workspace.")), null);
            }

            policyId = policy.Id;
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
