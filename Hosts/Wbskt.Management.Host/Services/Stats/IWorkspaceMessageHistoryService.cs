using Wbskt.Infrastructure;
using Wbskt.Management.Models;

namespace Wbskt.Management.Host.Services.Stats;

public interface IWorkspaceMessageHistoryService
{
    /// <summary>
    /// The workspace's client messages over [<paramref name="from"/>, <paramref name="to"/>) per hour or per
    /// day (<paramref name="bucket"/> <c>1h</c> or <c>1d</c>, picked from the range when left out). The range
    /// defaults to the last 24 hours.
    /// </summary>
    Task<Result<WorkspaceMessageHistory>> GetAsync(int workspaceId, DateTimeOffset? from, DateTimeOffset? to, string? bucket, CancellationToken ct);

    /// <summary>
    /// The same for one client of the workspace, as far back as the hourly counts are kept; <c>CLIENT_NOT_FOUND</c>
    /// for a client that is not the workspace's.
    /// </summary>
    Task<Result<WorkspaceMessageHistory>> GetForClientAsync(int workspaceId, Guid clientRef, DateTimeOffset? from, DateTimeOffset? to, string? bucket, CancellationToken ct);
}
