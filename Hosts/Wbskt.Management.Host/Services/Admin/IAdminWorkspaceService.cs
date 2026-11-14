using Wbskt.Infrastructure;
using Wbskt.Management.Models;
using Wbskt.Models.Admin;

namespace Wbskt.Management.Host.Services.Admin;

/// <summary>Changes the platform admin host makes to a workspace for a staff member, each with its staff audit row.</summary>
public interface IAdminWorkspaceService
{
    Task<Result<StaffActionResponse>> SetCreditCapAsync(Guid workspaceRef, AdminSetWorkspaceCreditCapRequest request, CancellationToken cancellationToken);
}
