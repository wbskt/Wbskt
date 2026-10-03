using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Management;
using Wbskt.Infrastructure;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Providers;
using Wbskt.Models;

namespace Wbskt.Management.Host.Services;

internal sealed class RegistrationPolicyService : IRegistrationPolicyService
{
    private readonly IRegistrationPolicyProvider _provider;
    private readonly IClientProvider _clientProvider;
    private readonly IEventBus _eventBus;
    private readonly ILogger<RegistrationPolicyService> _logger;

    public RegistrationPolicyService(
        IRegistrationPolicyProvider provider, 
        IClientProvider clientProvider,
        IEventBus eventBus,
        ILogger<RegistrationPolicyService> logger)
    {
        _provider = provider;
        _clientProvider = clientProvider;
        _eventBus = eventBus;
        _logger = logger;
    }

    public async Task<Result<IPagedList<RegistrationPolicyResponse>>> GetAllAsync(int workSpaceId, bool? autoApproval,
        string? name, int skip, int take, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Querying registration policies for WorkspaceId: {WorkspaceId}", workSpaceId);

        try
        {
            var pagedPolicies = await _provider.GetAllAsync(workSpaceId, autoApproval, name, skip, take, cancellationToken);
            _logger.LogTrace("Retrieved {Count} policies for WorkspaceId: {WorkspaceId}", pagedPolicies.TotalCount, workSpaceId);
            
            var result = new PagedList<RegistrationPolicyResponse>(pagedPolicies.Select(MapToResponse), pagedPolicies.TotalCount);
            return Result<IPagedList<RegistrationPolicyResponse>>.Success(result);
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to query registration policies for WorkspaceId: {WorkspaceId}. Error: {Message}", workSpaceId, ex.Message);
            _logger.LogTrace(ex, "GetAllAsync exception stack trace for WorkspaceId {WorkspaceId}", workSpaceId);
            return Result<IPagedList<RegistrationPolicyResponse>>.Failure(Error.Failure("POLICY_QUERY_ERROR", ex.Message));
        }
    }

    public async Task<Result<RegistrationPolicyResponse>> GetByRefIdAsync(Guid refId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Querying registration policy by RefId: '{RefId}'", refId);

        try
        {
            var policy = await _provider.GetByRefIdAsync(refId, cancellationToken);
            return Result<RegistrationPolicyResponse>.Success(MapToResponse(policy));
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Registration policy not found for RefId: '{RefId}'. Error: {Message}", refId, ex.Message);
            _logger.LogTrace(ex, "GetByRefIdAsync lookup failure stack trace for RefId: '{RefId}'", refId);
            return Result<RegistrationPolicyResponse>.Failure(Error.NotFound("POLICY_NOT_FOUND", "Registration policy not found."));
        }
    }

    public async Task<Result<RegistrationPolicy>> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Querying registration policy by ID: {PolicyId}", id);

        try
        {
            var policy = await _provider.GetByIdAsync(id, cancellationToken);
            return Result<RegistrationPolicy>.Success(policy);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Registration policy not found for ID: {PolicyId}. Error: {Message}", id, ex.Message);
            _logger.LogTrace(ex, "GetByIdAsync lookup failure stack trace for ID: {PolicyId}", id);
            return Result<RegistrationPolicy>.Failure(Error.NotFound("POLICY_NOT_FOUND", "Registration policy not found."));
        }
    }

    public async Task<Result<RegistrationPolicyResponse>> CreateAsync(int workspaceId, RegistrationPolicyRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Creating registration policy '{PolicyName}' in WorkspaceId: {WorkspaceId}", request.Name, workspaceId);

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            _logger.LogWarning("Registration policy creation failed: Name is empty");
            return Result<RegistrationPolicyResponse>.Failure(Error.Validation("POLICY_NAME_REQUIRED", "Policy name is required."));
        }

        try
        {
            var policy = await _provider.InsertAsync(workspaceId, request, cancellationToken);
            _logger.LogInformation("Registration policy '{PolicyName}' created successfully with RefId: {RefId}", request.Name, policy.RefId);
            
            await _eventBus.PublishAsync(new PolicyCreatedEvent(policy.RefId, policy.Id, workspaceId, policy.Name), cancellationToken);

            return Result<RegistrationPolicyResponse>.Success(MapToResponse(policy));
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to create registration policy '{PolicyName}' in WorkspaceId: {WorkspaceId}. Error: {Message}", request.Name, workspaceId, ex.Message);
            _logger.LogTrace(ex, "CreateAsync exception stack trace for '{PolicyName}'", request.Name);
            return Result<RegistrationPolicyResponse>.Failure(Error.Failure("POLICY_CREATE_ERROR", ex.Message));
        }
    }

    public async Task<Result> UpdateAsync(int workspaceId, int policyId, UpdateRegistrationPolicyRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Updating registration policy ID {PolicyId} in WorkspaceId: {WorkspaceId}", policyId, workspaceId);

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            _logger.LogWarning("Registration policy update failed: Name is empty");
            return Result.Failure(Error.Validation("POLICY_NAME_REQUIRED", "Policy name is required."));
        }

        try
        {
            RegistrationPolicy policy;
            try
            {
                policy = await _provider.GetByIdAsync(policyId, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to update policy status: Policy ID {PolicyId} not found. Error: {Message}", policyId, ex.Message);
                _logger.LogTrace(ex, "GetByIdAsync lookup failure stack trace for PolicyId {PolicyId}", policyId);
                return Result.Failure(Error.NotFound("POLICY_NOT_FOUND", "Registration policy not found."));
            }

            if (policy.WorkspaceId != workspaceId)
            {
                _logger.LogWarning("Policy update rejected: Policy ID {PolicyId} does not belong to WorkspaceId: {WorkspaceId}", policyId, workspaceId);
                return Result.Failure(Error.Forbidden("POLICY_UNAUTHORIZED", "Policy does not belong to this workspace."));
            }

            if (request.MaxClients.HasValue)
            {
                var currentCount = await _clientProvider.GetRegisteredCountByPolicyIdAsync(policyId, cancellationToken);
                if (currentCount > request.MaxClients.Value)
                {
                    _logger.LogWarning("Policy update failed: Current registered clients ({Current}) exceeds new limit ({Max}) for Policy ID {PolicyId}", currentCount, request.MaxClients.Value, policyId);
                    return Result.Failure(Error.Validation("POLICY_LIMIT_CONFLICT", $"Cannot set max clients to {request.MaxClients.Value} because {currentCount} clients are already registered under this policy."));
                }
            }

            await _provider.UpdateAsync(workspaceId, policyId, request, cancellationToken);
            _logger.LogInformation("Registration policy ID {PolicyId} updated successfully", policyId);

            var updatedPolicy = await _provider.GetByIdAsync(policyId, cancellationToken);
            var events = new List<BaseEvent>(2);
            if (!updatedPolicy.Name.Equals(request.Name))
            {
                events.Add(new PolicyNameUpdatedEvent(updatedPolicy.RefId, updatedPolicy.Id, workspaceId, request.Name, updatedPolicy.Name));
            }

            if (updatedPolicy.MaxClients != request.MaxClients)
            {
                events.Add(new PolicyClientLimitUpdatedEvent(updatedPolicy.RefId, updatedPolicy.Id, workspaceId, request.MaxClients, updatedPolicy.MaxClients));
            }

            await Parallel.ForEachAsync(events, cancellationToken, async (@event, token) => await _eventBus.PublishAsync(@event, token));

            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError("Unexpected error updating registration policy ID {PolicyId}. Error: {Message}", policyId, ex.Message);
            _logger.LogTrace(ex, "UpdateAsync exception stack trace for PolicyId {PolicyId}", policyId);
            return Result.Failure(Error.Failure("POLICY_UPDATE_ERROR", ex.Message));
        }
    }

    public async Task<Result<RegistrationPolicyResponse>> RotatePinAsync(int workspaceId, int policyId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Rotating PIN of registration policy ID {PolicyId} in WorkspaceId: {WorkspaceId}", policyId, workspaceId);

        try
        {
            RegistrationPolicy policy;
            try
            {
                policy = await _provider.GetByIdAsync(policyId, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to rotate PIN: Policy ID {PolicyId} not found. Error: {Message}", policyId, ex.Message);
                return Result<RegistrationPolicyResponse>.Failure(Error.NotFound("POLICY_NOT_FOUND", "Registration policy not found."));
            }

            if (policy.WorkspaceId != workspaceId)
            {
                _logger.LogWarning("PIN rotation rejected: Policy ID {PolicyId} does not belong to WorkspaceId: {WorkspaceId}", policyId, workspaceId);
                return Result<RegistrationPolicyResponse>.Failure(Error.Forbidden("POLICY_UNAUTHORIZED", "Policy does not belong to this workspace."));
            }

            var rotated = await _provider.RotatePinAsync(workspaceId, policyId, cancellationToken);
            _logger.LogInformation("Registration policy ID {PolicyId} has a new PIN", policyId);

            await _eventBus.PublishAsync(new PolicyPinRotatedEvent(rotated.RefId, rotated.Id, workspaceId), cancellationToken);

            return Result<RegistrationPolicyResponse>.Success(MapToResponse(rotated));
        }
        catch (Exception ex)
        {
            _logger.LogError("Unexpected error rotating PIN of registration policy ID {PolicyId}. Error: {Message}", policyId, ex.Message);
            _logger.LogTrace(ex, "RotatePinAsync exception stack trace for PolicyId {PolicyId}", policyId);
            return Result<RegistrationPolicyResponse>.Failure(Error.Failure("POLICY_UPDATE_ERROR", ex.Message));
        }
    }

    public async Task<Result> DisableAsync(int workspaceId, int policyId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Disabling registration policy ID {PolicyId} in WorkspaceId: {WorkspaceId}", policyId, workspaceId);

        try
        {
            RegistrationPolicy policy;
            try
            {
                policy = await _provider.GetByIdAsync(policyId, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to disable policy: Policy ID {PolicyId} not found. Error: {Message}", policyId, ex.Message);
                _logger.LogTrace(ex, "GetByIdAsync lookup failure stack trace for PolicyId {PolicyId}", policyId);
                return Result.Failure(Error.NotFound("POLICY_NOT_FOUND", "Registration policy not found."));
            }

            if (policy.WorkspaceId != workspaceId)
            {
                _logger.LogWarning("Policy disable rejected: Policy ID {PolicyId} does not belong to WorkspaceId: {WorkspaceId}", policyId, workspaceId);
                return Result.Failure(Error.Forbidden("POLICY_UNAUTHORIZED", "Policy does not belong to this workspace."));
            }

            await _provider.DisableAsync(workspaceId, policyId, cancellationToken);
            _logger.LogInformation("Registration policy ID {PolicyId} disabled successfully", policyId);

            await _eventBus.PublishAsync(new PolicyDisabledEvent(policy.RefId, policy.Id, workspaceId), cancellationToken);

            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError("Unexpected error disabling registration policy ID {PolicyId}. Error: {Message}", policyId, ex.Message);
            _logger.LogTrace(ex, "DisableAsync exception stack trace for PolicyId {PolicyId}", policyId);
            return Result.Failure(Error.Failure("POLICY_DISABLE_ERROR", ex.Message));
        }
    }

    private static RegistrationPolicyResponse MapToResponse(RegistrationPolicy p)
    {
        return new RegistrationPolicyResponse(
            p.RefId,
            p.Pin,
            p.Name,
            p.MaxClients,
            p.AutoApproval,
            p.IsEnabled,
            p.CreatedAt,
            p.RegisteredClientCount,
            p.ConnectedClientCount
        );
    }
}
