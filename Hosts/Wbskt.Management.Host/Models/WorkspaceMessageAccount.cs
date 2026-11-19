using Wbskt.Models.Billing;

namespace Wbskt.Management.Host.Models;

/// <summary>
/// A row of <c>dbo.WorkspaceMessageUsage_GetAccounts</c>: a workspace, its plan copy (none yet: Free) and
/// the client messages its account carried this month.
/// </summary>
/// <param name="TenantId">The account's tenant; null while the workspace's plan copy has not arrived, when the workspace is its own account.</param>
/// <param name="WorkspaceMessages">What the workspace carried itself, of <c>AccountMessages</c>.</param>
public sealed record WorkspaceMessageAccount(int WorkspaceId, PlanCode PlanCode, PlanLimitOverrides? LimitOverrides, long AccountMessages, int? TenantId = null, long WorkspaceMessages = 0);
