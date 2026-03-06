using Wbskt.Common.Abstraction.Models;
using Wbskt.Common.Abstraction.Models.Management;

namespace Wbskt.Management.Host.Services;

public interface IClientService
{
    Task<IPagedList<ClientResponse>> GetAllAsync(ClientStatus? status, string? name, int skip, int take, CancellationToken cancellationToken = default);
    Task<IPagedList<ClientResponse>> GetByPolicyIdAsync(int policyId, ClientStatus? status, string? name, int skip, int take, CancellationToken cancellationToken = default);
    Task UpdateStatusAsync(int workspaceId, int id, ClientStatus status, CancellationToken cancellationToken = default);
}
