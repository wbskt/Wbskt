using Webskt.Management.Host.Models;
using Webskt.Management.Host.Providers;
using Webskt.Common.Abstraction.Exceptions;

namespace Webskt.Management.Host.Services;

public class RegistrationPolicyService : IRegistrationPolicyService
{
    private readonly IRegistrationPolicyProvider _provider;

    public RegistrationPolicyService(IRegistrationPolicyProvider provider)
    {
        _provider = provider;
    }

    public async Task<IReadOnlyCollection<RegistrationPolicyResponse>> GetAllAsync()
    {
        var policies = await _provider.GetAllAsync();
        
        return policies.Select(MapToResponse).ToList().AsReadOnly();
    }

    public async Task<RegistrationPolicyResponse> GetByRefIdAsync(Guid refId)
    {
        var policy = await _provider.GetByRefIdAsync(refId);
        
        return MapToResponse(policy);
    }

    public async Task<RegistrationPolicyResponse> CreateAsync(RegistrationPolicyRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ValidationException("Policy name is required.");
        }

        var policy = await _provider.InsertAsync(request);
        
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
