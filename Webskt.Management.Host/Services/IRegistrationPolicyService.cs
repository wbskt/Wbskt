using Webskt.Common.Abstraction.Models;
using Webskt.Common.Abstraction.Models.Management;

namespace Webskt.Management.Host.Services;

public interface IRegistrationPolicyService
{
    Task<IPagedList<RegistrationPolicyResponse>> GetAllAsync(bool? autoApproval, string? name, int skip, int take, CancellationToken cancellationToken = default);
    Task<RegistrationPolicyResponse> GetByRefIdAsync(Guid refId, CancellationToken cancellationToken = default);
    Task<RegistrationPolicyResponse> CreateAsync(int workspaceId, RegistrationPolicyRequest request, CancellationToken cancellationToken = default);
}
