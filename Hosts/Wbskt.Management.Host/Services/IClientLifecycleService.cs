using Wbskt.Infrastructure;
using Wbskt.Management.Host.Models;
using Wbskt.Models;

namespace Wbskt.Management.Host.Services;

/// <summary>Changes to a client: status, name, tags, secret, and deletion.</summary>
public interface IClientLifecycleService
{
    Task<Result> UpdateStatusAsync(int workspaceId, Guid clientRefId, ClientStatus status, CancellationToken cancellationToken = default);
    /// <summary>Applies one status to many clients, each as <see cref="UpdateStatusAsync"/> would.</summary>
    Task<Result<BulkClientStatusResponse>> UpdateStatusesAsync(int workspaceId, IReadOnlyList<Guid> clientRefIds, ClientStatus status, CancellationToken cancellationToken = default);
    /// <summary>Deletes a client, its capabilities and state, and disconnects it. Its event log stays.</summary>
    Task<Result> DeleteAsync(int workspaceId, Guid clientRefId, CancellationToken cancellationToken = default);
    /// <summary>Replaces a client's secret, returns the new one once, and disconnects it.</summary>
    Task<Result<ClientSecretResponse>> RotateSecretAsync(int workspaceId, Guid clientRefId, CancellationToken cancellationToken = default);
    Task<Result> RenameAsync(int workspaceId, Guid clientRefId, string name, CancellationToken cancellationToken = default);
    /// <summary>
    /// Replaces a client's tags. Each is normalised (trimmed, lower-cased) and duplicates collapse;
    /// an empty list clears them. Returns the tags as stored, sorted.
    /// </summary>
    Task<Result<ClientTagsResponse>> SetTagsAsync(int workspaceId, Guid clientRefId, IReadOnlyList<string>? tags, CancellationToken cancellationToken = default);
}
