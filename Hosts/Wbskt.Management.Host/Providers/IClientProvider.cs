using Wbskt.Protocol;
using Wbskt.Management.Host.Models;
using Wbskt.Models;
using Wbskt.Primitives;

namespace Wbskt.Management.Host.Providers;

public interface IClientProvider : IReferenceProvider
{
    Task<int> GetRegisteredCountByPolicyIdAsync(int policyId, CancellationToken cancellationToken = default);
    /// <summary><paramref name="tag"/>, when given, must already be normalised (<see cref="ClientTags.TryNormalize"/>).</summary>
    Task<IPagedList<Client>> GetAllAsync(int workspaceId, ClientStatus? status, string? name, string? tag, int skip, int take,
        CancellationToken cancellationToken = default);
    Task<IPagedList<Client>> GetByPolicyIdAsync(int workspaceId, int policyId, ClientStatus? status, string? name,
        string? tag, int skip, int take, CancellationToken cancellationToken = default);
    /// <summary>
    /// Creates a client. Throws <c>SqlException</c> 50020 when its policy is at <c>MaxClients</c>, and
    /// 50025 when it would be created registered and the workspace already has
    /// <paramref name="maxPerWorkspace"/> registered clients (null is no ceiling). A pending client is
    /// not checked against the workspace ceiling until it is approved.
    /// </summary>
    Task<Client> InsertClientAsync(int workspaceId, int policyId, string name, byte[] secretHash, ClientStatus status,
        int? maxPerWorkspace, CancellationToken cancellationToken = default);
    /// <summary>
    /// The client plus its stored secret hash, for authentication. Comparison happens in the
    /// service, not here and not in SQL - see <c>ClientSecrets.Matches</c>.
    /// </summary>
    Task<ClientCredential?> FindCredentialAsync(Guid refId, CancellationToken cancellationToken = default);
    /// <summary>
    /// Sets <paramref name="status"/> on each client in <paramref name="clientRefIds"/> that belongs to
    /// the workspace, in one call, and returns one result per reference in the same order. When
    /// approvals compete for a policy's last places, the earlier references get them. Approvals that
    /// would take the workspace past <paramref name="maxPerWorkspace"/> registered clients (null is no
    /// ceiling) are refused the same way.
    /// </summary>
    Task<IReadOnlyList<ClientStatusChange>> UpdateStatusesAsync(int workspaceId, IReadOnlyList<Guid> clientRefIds, ClientStatus status,
        int? maxPerWorkspace, CancellationToken cancellationToken = default);
    /// <summary>Deletes a client in <paramref name="workspaceId"/>; false when there was none to delete.</summary>
    Task<bool> DeleteAsync(int id, int workspaceId, CancellationToken cancellationToken = default);
    /// <summary>
    /// Replaces a client's tags with <paramref name="tags"/> (normalised, distinct); false when the
    /// client is not in <paramref name="workspaceId"/>.
    /// </summary>
    Task<bool> SetTagsAsync(int id, int workspaceId, IReadOnlyCollection<string> tags, CancellationToken cancellationToken = default);
    /// <summary>Every tag in use in the workspace with its client count, by tag.</summary>
    Task<IReadOnlyList<ClientTagCount>> GetTagsAsync(int workspaceId, CancellationToken cancellationToken = default);
    /// <summary>Replaces a client's secret hash; false when the client is not in <paramref name="workspaceId"/>.</summary>
    Task<bool> UpdateSecretAsync(int id, int workspaceId, byte[] secretHash, CancellationToken cancellationToken = default);
    Task UpdatePresenceAsync(int id, bool isConnected, DateTime lastActivityAt, string? hostId = null, CancellationToken cancellationToken = default);
    Task ResetAllPresenceAsync(string? hostId = null, CancellationToken cancellationToken = default);
    /// <summary>
    /// Clears the presence of clients held by a socket host not heard from for <paramref name="staleAfter"/>,
    /// and returns them. Each row is claimed once, however many instances sweep at the same time.
    /// </summary>
    Task<IReadOnlyCollection<ExpiredPresence>> ExpirePresenceAsync(TimeSpan staleAfter, CancellationToken cancellationToken = default);
    Task<ClientDetail?> FindDetailByRefIdAsync(Guid refId, CancellationToken cancellationToken = default);
    Task UpdateNameAsync(int id, string name, CancellationToken cancellationToken = default);
    Task UpdateRttAsync(int id, int lastRttMs, DateTime measuredAt, CancellationToken cancellationToken = default);
    Task UpsertCapabilitiesAsync(int clientId, string agentName, string agentVersion, string platform,
        string capabilitiesJson, CancellationToken cancellationToken = default);
    Task<StateVariableUpsert> UpsertStateVariableAsync(int clientId, string name, string dataType, string valueJson,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<ClientStateVariable>> GetStateVariablesAsync(int clientId, CancellationToken cancellationToken = default);
}
