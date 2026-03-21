using Wbskt.Management.Host.Models;
using Wbskt.Models;

namespace Wbskt.Management.Host.Services;

public interface IClientService
{
    Task<IPagedList<ClientResponse>> GetAllAsync(int workspaceId, ClientStatus? status, string? name, int skip,
        int take, CancellationToken cancellationToken = default);
    Task<IPagedList<ClientResponse>> GetByPolicyIdAsync(int workspaceId, int policyId, ClientStatus? status,
        string? name, int skip, int take, CancellationToken cancellationToken = default);
    Task UpdateStatusAsync(int workspaceId, int id, ClientStatus status, CancellationToken cancellationToken = default);
}
