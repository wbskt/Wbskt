using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Management;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Providers;
using Wbskt.Models;
using Wbskt.Primitives.Exceptions;

namespace Wbskt.Management.Host.Services;

internal sealed class RegistrationPolicyService : IRegistrationPolicyService
{
    private readonly IRegistrationPolicyProvider _provider;
    private readonly IClientProvider _clientProvider;
    private readonly IEventBus _eventBus;

    public RegistrationPolicyService(
        IRegistrationPolicyProvider provider, 
        IClientProvider clientProvider,
        IEventBus eventBus)
    {
        _provider = provider;
        _clientProvider = clientProvider;
        _eventBus = eventBus;
    }

    public async Task<IPagedList<RegistrationPolicyResponse>> GetAllAsync(int workSpaceId, bool? autoApproval,
        string? name, int skip, int take, CancellationToken cancellationToken = default)
    {
        var pagedPolicies = await _provider.GetAllAsync(workSpaceId, autoApproval, name, skip, take, cancellationToken);
        
        return new PagedList<RegistrationPolicyResponse>(pagedPolicies.Select(MapToResponse), pagedPolicies.TotalCount);
    }

    public async Task<RegistrationPolicyResponse> GetByRefIdAsync(Guid refId, CancellationToken cancellationToken = default)
    {
        var policy = await _provider.GetByRefIdAsync(refId, cancellationToken);
        
        return MapToResponse(policy);
    }

    public async Task<RegistrationPolicy> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _provider.GetByIdAsync(id, cancellationToken);
    }

    public async Task<RegistrationPolicyResponse> CreateAsync(int workspaceId, RegistrationPolicyRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ValidationException("Policy name is required.");
        }

        var policy = await _provider.InsertAsync(workspaceId, request, cancellationToken);
        
        await _eventBus.PublishAsync(new PolicyCreatedEvent(policy.RefId, policy.Id, workspaceId, policy.Name), cancellationToken);

        return MapToResponse(policy);
    }

    public async Task UpdateAsync(int workspaceId, int policyId, UpdateRegistrationPolicyRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ValidationException("Policy name is required.");
        }

        if (request.MaxClients.HasValue)
        {
            var currentCount = await _clientProvider.GetRegisteredCountByPolicyIdAsync(policyId, cancellationToken);
            
            // Logic requiring an empty line before this comment
            if (currentCount > request.MaxClients.Value)
            {
                throw new ValidationException($"Cannot set max clients to {request.MaxClients.Value} because {currentCount} clients are already registered under this policy.");
            }
        }

        await _provider.UpdateAsync(workspaceId, policyId, request, cancellationToken);

        var policy = await _provider.GetByIdAsync(policyId, cancellationToken);
        var events = new List<BaseEvent>(2);
        if (!policy.Name.Equals(request.Name))
        {
            events.Add(new PolicyNameUpdatedEvent(policy.RefId, policy.Id, workspaceId, request.Name, policy.Name));
        }

        if (policy.MaxClients != request.MaxClients)
        {
            events.Add(new PolicyClientLimitUpdatedEvent(policy.RefId, policy.Id, workspaceId, request.MaxClients, policy.MaxClients));
        }

        await Parallel.ForEachAsync(events, cancellationToken, async (@event, token) => await _eventBus.PublishAsync(@event, token));
    }

    public async Task DisableAsync(int workspaceId, int policyId, CancellationToken cancellationToken = default)
    {
        await _provider.DisableAsync(workspaceId, policyId, cancellationToken);

        var policy = await _provider.GetByIdAsync(policyId, cancellationToken);
        await _eventBus.PublishAsync(new PolicyDisabledEvent(policy.RefId, policy.Id, workspaceId), cancellationToken);
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
            p.CreatedAt
        );
    }
}
