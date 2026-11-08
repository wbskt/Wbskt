using Wbskt.Models.Billing;

namespace Wbskt.Infrastructure.Billing;

/// <summary>
/// The main database's copy of the plan a workspace's tenant is on (<c>dbo.WorkspaceAccounts</c>),
/// kept by the management host from the auth host, which owns it.
/// </summary>
/// <param name="PlanRevision">
/// The tenant's plan revision this copy was taken at. A copy is only ever replaced by one at the same
/// or a later revision, so a slow sync cannot put back a plan a newer change replaced.
/// </param>
public sealed record WorkspaceAccount(
    int WorkspaceId,
    int TenantId,
    Guid TenantRefId,
    PlanCode PlanCode,
    PlanLimitOverrides? LimitOverrides,
    int PlanRevision);
