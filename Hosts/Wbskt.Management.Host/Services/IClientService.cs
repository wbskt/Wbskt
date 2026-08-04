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
    Task<Result<ClientDetailResponse>> GetDetailAsync(int workspaceId, Guid clientRefId, CancellationToken cancellationToken = default);
    Task<Result> RenameAsync(int workspaceId, int id, string name, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyCollection<ClientStateVariableResponse>>> GetStateAsync(int workspaceId, int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies a client reference belongs to <paramref name="workspaceId"/> and returns its internal
    /// ID. For endpoints that act on a client without going through a workspace-scoped service call —
    /// publishing to the bus, most of all, since the socket host dispatches purely on
    /// <c>ClientRefId</c> and has no workspace of its own to check against.
    /// </summary>
    Task<Result<int>> EnsureClientInWorkspaceAsync(int workspaceId, Guid clientRefId, CancellationToken cancellationToken = default);
}
