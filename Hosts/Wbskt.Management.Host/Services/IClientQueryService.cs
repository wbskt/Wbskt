using Wbskt.Infrastructure;
using Wbskt.Management.Host.Models;
using Wbskt.Models;

namespace Wbskt.Management.Host.Services;

/// <summary>Reads of a workspace's clients, and the ownership checks other services build on.</summary>
public interface IClientQueryService
{
    /// <summary><paramref name="tag"/>, when given, lists only clients carrying it; an invalid one is <c>CLIENT_TAG_INVALID</c>.</summary>
    Task<Result<IPagedList<ClientResponse>>> GetAllAsync(int workspaceId, ClientStatus? status, string? name, string? tag, int skip,
        int take, CancellationToken cancellationToken = default);
    Task<Result<IPagedList<ClientResponse>>> GetByPolicyIdAsync(int workspaceId, int policyId, ClientStatus? status,
        string? name, string? tag, int skip, int take, CancellationToken cancellationToken = default);
    Task<Result<ClientDetailResponse>> GetDetailAsync(int workspaceId, Guid clientRefId, CancellationToken cancellationToken = default);
    /// <summary>Every tag in use in the workspace, with how many clients carry it.</summary>
    Task<Result<IReadOnlyList<ClientTagCountResponse>>> GetTagsAsync(int workspaceId, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyCollection<ClientStateVariableResponse>>> GetStateAsync(int workspaceId, Guid clientRefId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies a client reference belongs to <paramref name="workspaceId"/> and returns its internal
    /// ID. For endpoints that act on a client without going through a workspace-scoped service call —
    /// publishing to the bus, most of all, since the socket host dispatches purely on
    /// <c>ClientRefId</c> and has no workspace of its own to check against.
    /// <para>
    /// A reference that does not resolve and one belonging to another workspace fail identically, as
    /// <c>CLIENT_NOT_FOUND</c>. Callers must not reinstate the distinction: telling the two apart
    /// confirms that a guessed reference names a real client.
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
