using Webskt.Common.Abstraction.Models.Management;
using Webskt.Management.Host.Models;
using Webskt.Management.Host.Providers;
using Webskt.Common.Abstraction.Exceptions;
using Webskt.Common.Abstraction.Models;

namespace Webskt.Management.Host.Services;

internal sealed class RegistrationPolicyService : IRegistrationPolicyService
{
    private readonly IRegistrationPolicyProvider _provider;

    public RegistrationPolicyService(IRegistrationPolicyProvider provider)
    {
        _provider = provider;
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
