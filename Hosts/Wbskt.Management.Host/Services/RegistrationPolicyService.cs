using Wbskt.Common.Abstraction.Models;
using Wbskt.Common.Abstraction.Models.Management;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Management;
using Wbskt.Foundation.Abstraction.Exceptions;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Providers;

namespace Wbskt.Management.Host.Services;

internal sealed class RegistrationPolicyService : IRegistrationPolicyService
{
    private readonly IRegistrationPolicyProvider _provider;
    private readonly IEventBus _eventBus;

    public RegistrationPolicyService(IRegistrationPolicyProvider provider, IEventBus eventBus)
    {
        _provider = provider;
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
        
        await _eventBus.PublishAsync(new PolicyCreatedEvent(policy.RefId, workspaceId, policy.Name), cancellationToken);

        return MapToResponse(policy);
    }

    private static RegistrationPolicyResponse MapToResponse(RegistrationPolicy p)
    {
        return new RegistrationPolicyResponse(
            p.RefId,
            p.Pin,
            p.Name,
            p.MaxClients,
            p.AutoApproval,
            p.CreatedAt
        );
    }
}
