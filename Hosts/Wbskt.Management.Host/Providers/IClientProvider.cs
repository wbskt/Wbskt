using Wbskt.Management.Host.Models;
using Wbskt.Models;
using Wbskt.Primitives;

namespace Wbskt.Management.Host.Providers;

public interface IClientProvider : IReferenceProvider
{
    Task<int> GetRegisteredCountByPolicyIdAsync(int policyId, CancellationToken cancellationToken = default);
    Task<Client> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<IPagedList<Client>> GetAllAsync(int workspaceId, ClientStatus? status, string? name, int skip, int take,
        CancellationToken cancellationToken = default);
    Task<IPagedList<Client>> GetByPolicyIdAsync(int workspaceId, int policyId, ClientStatus? status, string? name,
        int skip, int take, CancellationToken cancellationToken = default);
    Task<Client> InsertClientAsync(int workspaceId, int policyId, string name, byte[] secretHash, ClientStatus status,
        CancellationToken cancellationToken = default);
    /// <summary>
    /// The client plus its stored secret hash, for authentication. Comparison happens in the
    /// service, not here and not in SQL - see <c>ClientSecrets.Matches</c>.
    /// </summary>
    Task<ClientCredential> GetCredentialAsync(Guid refId, CancellationToken cancellationToken = default);
    /// <summary>
    /// Approving (<see cref="ClientStatus.Registered"/>) throws a <c>SqlException</c> numbered 50020
    /// when the policy is already at its client limit; the procedure checks under a lock on the policy.
    /// </summary>
    Task UpdateStatusAsync(int id, ClientStatus status, CancellationToken cancellationToken = default);
    /// <summary>Deletes a client in <paramref name="workspaceId"/>; false when there was none to delete.</summary>
    Task<bool> DeleteAsync(int id, int workspaceId, CancellationToken cancellationToken = default);
    /// <summary>Replaces a client's secret hash; false when the client is not in <paramref name="workspaceId"/>.</summary>
    Task<bool> UpdateSecretAsync(int id, int workspaceId, byte[] secretHash, CancellationToken cancellationToken = default);
    Task UpdatePresenceAsync(int id, bool isConnected, DateTime lastActivityAt, string? hostId = null, CancellationToken cancellationToken = default);
    Task ResetAllPresenceAsync(string? hostId = null, CancellationToken cancellationToken = default);
    Task<ClientDetail> GetDetailByRefIdAsync(Guid refId, CancellationToken cancellationToken = default);
    Task UpdateNameAsync(int id, string name, CancellationToken cancellationToken = default);
    Task UpdateRttAsync(int id, int lastRttMs, DateTime measuredAt, CancellationToken cancellationToken = default);
    Task UpsertCapabilitiesAsync(int clientId, string agentName, string agentVersion, string platform,
        string capabilitiesJson, CancellationToken cancellationToken = default);
    Task<StateVariableUpsert> UpsertStateVariableAsync(int clientId, string name, string dataType, string valueJson,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<ClientStateVariable>> GetStateVariablesAsync(int clientId, CancellationToken cancellationToken = default);
}
