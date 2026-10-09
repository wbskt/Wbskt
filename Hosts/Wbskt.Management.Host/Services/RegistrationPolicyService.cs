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

        var pagedPolicies = await _provider.GetAllAsync(workSpaceId, autoApproval, name, skip, take, cancellationToken);
        _logger.LogTrace("Retrieved {Count} policies for WorkspaceId: {WorkspaceId}", pagedPolicies.TotalCount, workSpaceId);
        
        var result = new PagedList<RegistrationPolicyResponse>(pagedPolicies.Select(MapToResponse), pagedPolicies.TotalCount);
        return Result<IPagedList<RegistrationPolicyResponse>>.Success(result);
    }

    public async Task<Result<RegistrationPolicy>> FindInWorkspaceAsync(int workspaceId, Guid refId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Querying registration policy '{RefId}' in WorkspaceId: {WorkspaceId}", refId, workspaceId);

        var lookup = await WorkspaceOwnership.LoadAsync(workspaceId, () => _provider.FindByRefIdAsync(refId, cancellationToken), WorkspaceOwnership.PolicyNotFound);
        if (lookup.IsFailure)
        {
            _logger.LogWarning("Registration policy '{RefId}' not found in WorkspaceId: {WorkspaceId}", refId, workspaceId);
        }

        return lookup;
    }

    public async Task<Result<RegistrationPolicyResponse>> GetAsync(int workspaceId, Guid policyRefId, CancellationToken cancellationToken = default)
    {
        var policy = await FindInWorkspaceAsync(workspaceId, policyRefId, cancellationToken);
        return policy.IsSuccess
            ? Result<RegistrationPolicyResponse>.Success(MapToResponse(policy.Value))
            : Result<RegistrationPolicyResponse>.Failure(policy.Error);
    }

    public async Task<Result<RegistrationPolicyResponse>> CreateAsync(int workspaceId, RegistrationPolicyRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Creating registration policy '{PolicyName}' in WorkspaceId: {WorkspaceId}", request.Name, workspaceId);

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            _logger.LogWarning("Registration policy creation failed: Name is empty");
            return Result<RegistrationPolicyResponse>.Failure(Error.Validation("POLICY_NAME_REQUIRED", "Policy name is required."));
        }

        var policy = await _provider.InsertAsync(workspaceId, request, cancellationToken);
        _logger.LogInformation("Registration policy '{PolicyName}' created successfully with RefId: {RefId}", request.Name, policy.RefId);
        
        await _eventBus.PublishAsync(new PolicyCreatedEvent(policy.RefId, policy.Id, workspaceId, policy.Name), cancellationToken);

        return Result<RegistrationPolicyResponse>.Success(MapToResponse(policy));
    }

    public async Task<Result> UpdateAsync(int workspaceId, Guid policyRefId, UpdateRegistrationPolicyRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Updating registration policy '{PolicyRefId}' in WorkspaceId: {WorkspaceId}", policyRefId, workspaceId);

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            _logger.LogWarning("Registration policy update failed: Name is empty");
            return Result.Failure(Error.Validation("POLICY_NAME_REQUIRED", "Policy name is required."));
        }

        var lookup = await WorkspaceOwnership.LoadAsync(workspaceId, () => _provider.FindByRefIdAsync(policyRefId, cancellationToken), WorkspaceOwnership.PolicyNotFound);
        if (lookup.IsFailure)
        {
            _logger.LogWarning("Policy update rejected: Policy '{PolicyRefId}' not found in WorkspaceId: {WorkspaceId}", policyRefId, workspaceId);
            return Result.Failure(lookup.Error);
        }

        var policy = lookup.Value;
        var policyId = policy.Id;

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

        var updatedPolicy = await _provider.FindByIdAsync(policyId, cancellationToken)
            ?? throw new InvalidOperationException($"Policy {policyId} vanished during its own update.");
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

    public async Task<Result<RegistrationPolicyResponse>> RotatePinAsync(int workspaceId, Guid policyRefId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Rotating PIN of registration policy '{PolicyRefId}' in WorkspaceId: {WorkspaceId}", policyRefId, workspaceId);

        var lookup = await WorkspaceOwnership.LoadAsync(workspaceId, () => _provider.FindByRefIdAsync(policyRefId, cancellationToken), WorkspaceOwnership.PolicyNotFound);
        if (lookup.IsFailure)
        {
            _logger.LogWarning("PIN rotation rejected: Policy '{PolicyRefId}' not found in WorkspaceId: {WorkspaceId}", policyRefId, workspaceId);
            return Result<RegistrationPolicyResponse>.Failure(lookup.Error);
        }

        var policy = lookup.Value;
        var policyId = policy.Id;

        var rotated = await _provider.RotatePinAsync(workspaceId, policyId, cancellationToken);
        _logger.LogInformation("Registration policy ID {PolicyId} has a new PIN", policyId);

        await _eventBus.PublishAsync(new PolicyPinRotatedEvent(rotated.RefId, rotated.Id, workspaceId), cancellationToken);

        return Result<RegistrationPolicyResponse>.Success(MapToResponse(rotated));
    }

    public async Task<Result> DisableAsync(int workspaceId, Guid policyRefId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Disabling registration policy '{PolicyRefId}' in WorkspaceId: {WorkspaceId}", policyRefId, workspaceId);

        var lookup = await WorkspaceOwnership.LoadAsync(workspaceId, () => _provider.FindByRefIdAsync(policyRefId, cancellationToken), WorkspaceOwnership.PolicyNotFound);
        if (lookup.IsFailure)
        {
            _logger.LogWarning("Policy disable rejected: Policy '{PolicyRefId}' not found in WorkspaceId: {WorkspaceId}", policyRefId, workspaceId);
            return Result.Failure(lookup.Error);
        }

        var policy = lookup.Value;
        var policyId = policy.Id;

        await _provider.DisableAsync(workspaceId, policyId, cancellationToken);
        _logger.LogInformation("Registration policy ID {PolicyId} disabled successfully", policyId);

        await _eventBus.PublishAsync(new PolicyDisabledEvent(policy.RefId, policy.Id, workspaceId), cancellationToken);

        return Result.Success();
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
