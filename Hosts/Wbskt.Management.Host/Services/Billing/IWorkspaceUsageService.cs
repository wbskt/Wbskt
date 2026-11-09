using Wbskt.Infrastructure;
using Wbskt.Management.Models;

namespace Wbskt.Management.Host.Services.Billing;

public interface IWorkspaceUsageService
{
    /// <summary>
    /// The workspace's plan and what it and its account have used this month, with the workspace's
    /// credits per day from <paramref name="from"/> to <paramref name="to"/> (default: the last 31 days).
    /// </summary>
    Task<Result<WorkspaceUsageResponse>> GetAsync(int workspaceId, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct);
}
