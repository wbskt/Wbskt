using Webskt.Common.Abstraction.Events;
using Webskt.Common.Abstraction.Exceptions;
using Webskt.Common.Abstraction.Models;
using Webskt.Common.Abstraction.Models.Management;
using Webskt.Management.Host.Events;
using Webskt.Management.Host.Models;
using Webskt.Management.Host.Providers;

namespace Webskt.Management.Host.Services;

internal sealed class RegistrationPolicyService : IRegistrationPolicyService
{
    private readonly IRegistrationPolicyProvider _provider;
    private readonly IEventBus _eventBus;

    public RegistrationPolicyService(IRegistrationPolicyProvider provider, IEventBus eventBus)
    {
        _provider = provider;
        _eventBus = eventBus;
    }

    public async Task<IPagedList<RegistrationPolicyResponse>> GetAllAsync(bool? autoApproval, string? name, int skip, int take, CancellationToken cancellationToken = default)
    {
        var pagedPolicies = await _provider.GetAllAsync(autoApproval, name, skip, take, cancellationToken);
        
        return new PagedList<RegistrationPolicyResponse>(pagedPolicies.Select(MapToResponse), pagedPolicies.TotalCount);
    }

    public async Task<RegistrationPolicyResponse> GetByRefIdAsync(Guid refId, CancellationToken cancellationToken = default)
    {
        var policy = await _provider.GetByRefIdAsync(refId, cancellationToken);
        
        return MapToResponse(policy);
    }

    public async Task<RegistrationPolicyResponse> CreateAsync(RegistrationPolicyRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ValidationException("Policy name is required.");
        }

        var policy = await _provider.InsertAsync(request, cancellationToken);
        
        await _eventBus.PublishAsync(new PolicyCreatedEvent(policy.RefId, policy.Name), cancellationToken);

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
