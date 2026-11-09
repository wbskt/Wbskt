using Wbskt.Infrastructure;
using Wbskt.Management.Models;

namespace Wbskt.Management.Host.Services.Billing;

public interface IWorkspaceCreditCapService
{
    /// <summary>Sets or removes the workspace's monthly credit cap.</summary>
    Task<Result<WorkspaceCreditCapResponse>> SetAsync(int workspaceId, WorkspaceCreditCapRequest request, CancellationToken ct);
}
