using Wbskt.Common.Abstraction.Models;
using Wbskt.Common.Abstraction.Models.Management;
using Wbskt.Foundation.Abstraction;
using Wbskt.Management.Host.Models;

namespace Wbskt.Management.Host.Providers;

public interface IClientProvider : IReferenceProvider
{
    Task<int> GetRegisteredCountByPolicyIdAsync(int policyId, CancellationToken cancellationToken = default);
    Task<Client> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<IPagedList<Client>> GetAllAsync(int workspaceId, ClientStatus? status, string? name, int skip, int take,
        CancellationToken cancellationToken = default);
    Task<IPagedList<Client>> GetByPolicyIdAsync(int workspaceId, int policyId, ClientStatus? status, string? name,
        int skip, int take, CancellationToken cancellationToken = default);
    Task<Client> InsertClientAsync(int workspaceId, int policyId, string name, string secret, ClientStatus status,
        CancellationToken cancellationToken = default);
    Task<Client> VerifyAsync(Guid refId, string secret, CancellationToken cancellationToken = default);
    Task UpdateStatusAsync(int id, ClientStatus status, CancellationToken cancellationToken = default);
    Task UpdatePresenceAsync(int id, bool isConnected, DateTime lastActivityAt, CancellationToken cancellationToken = default);
}
