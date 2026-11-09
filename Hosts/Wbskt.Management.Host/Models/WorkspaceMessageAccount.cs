using Wbskt.Models.Billing;

namespace Wbskt.Management.Host.Models;

/// <summary>
/// A row of <c>dbo.WorkspaceMessageUsage_GetAccounts</c>: a workspace, its plan copy (none yet: Free) and
/// the client messages its account carried this month.
/// </summary>
public sealed record WorkspaceMessageAccount(int WorkspaceId, PlanCode PlanCode, PlanLimitOverrides? LimitOverrides, long AccountMessages);
