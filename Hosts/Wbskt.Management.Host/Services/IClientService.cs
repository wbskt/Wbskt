using Wbskt.Infrastructure;
using Wbskt.Management.Host.Models;
using Wbskt.Models;

namespace Wbskt.Management.Host.Services;

public interface IClientService
{
    Task<Result<IPagedList<ClientResponse>>> GetAllAsync(int workspaceId, ClientStatus? status, string? name, int skip,
        int take, CancellationToken cancellationToken = default);
    Task<Result<IPagedList<ClientResponse>>> GetByPolicyIdAsync(int workspaceId, int policyId, ClientStatus? status,
        string? name, int skip, int take, CancellationToken cancellationToken = default);
    Task<Result> UpdateStatusAsync(int workspaceId, int id, ClientStatus status, CancellationToken cancellationToken = default);
    /// <summary>Applies one status to many clients, each as <see cref="UpdateStatusAsync"/> would.</summary>
    Task<Result<BulkClientStatusResponse>> UpdateStatusesAsync(int workspaceId, IReadOnlyList<Guid> clientRefIds, ClientStatus status, CancellationToken cancellationToken = default);
    /// <summary>Deletes a client, its capabilities and state, and disconnects it. Its event log stays.</summary>
    Task<Result> DeleteAsync(int workspaceId, Guid clientRefId, CancellationToken cancellationToken = default);
    /// <summary>Replaces a client's secret, returns the new one once, and disconnects it.</summary>
    Task<Result<ClientSecretResponse>> RotateSecretAsync(int workspaceId, Guid clientRefId, CancellationToken cancellationToken = default);
    Task<Result<ClientDetailResponse>> GetDetailAsync(int workspaceId, Guid clientRefId, CancellationToken cancellationToken = default);
    Task<Result> RenameAsync(int workspaceId, int id, string name, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyCollection<ClientStateVariableResponse>>> GetStateAsync(int workspaceId, int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies a client reference belongs to <paramref name="workspaceId"/> and returns its internal
    /// ID. For endpoints that act on a client without going through a workspace-scoped service call —
    /// publishing to the bus, most of all, since the socket host dispatches purely on
    /// <c>ClientRefId</c> and has no workspace of its own to check against.
    /// <para>
    /// A reference that does not resolve and one belonging to another workspace fail identically, as
    /// a single <c>CLIENT_UNAUTHORIZED</c> forbidden error. Callers must not reinstate the
    /// distinction: telling the two apart confirms that a guessed reference names a real client.
    /// </para>
    /// </summary>
    Task<Result<int>> EnsureClientInWorkspaceAsync(int workspaceId, Guid clientRefId, CancellationToken cancellationToken = default);

    /// <summary>
    /// <see cref="EnsureClientInWorkspaceAsync"/> for a command, which also needs the device to be
    /// online: commands are delivered live or not at all, so an offline device is a
    /// <c>DEVICE_OFFLINE</c> conflict here rather than a command that silently goes nowhere. On
    /// success the target names the socket-host instance holding the connection.
    /// </summary>
    Task<Result<ClientCommandTarget>> ResolveCommandTargetAsync(int workspaceId, Guid clientRefId, CancellationToken cancellationToken = default);
}
