using Wbskt.Models.Billing;

namespace Wbskt.Auth.Models;

// The auth host's internal API (InternalRoutes), called by the management host over the backend
// network. Internal ids are their point, so nothing public may carry them.

public record ResolveWorkspaceRequest(Guid WorkspaceRef);

public record ResolvedWorkspaceResponse(int WorkspaceId, string[] Permissions);

/// <summary>The caller's internal user id (<c>InternalRoutes.CurrentUser</c>).</summary>
public record CurrentUserResponse(int UserId);

/// <summary>The workspaces deleted in the window asked for (<c>InternalRoutes.DeletedWorkspaces</c>).</summary>
public record DeletedWorkspacesResponse(int[] WorkspaceIds);

/// <summary>
/// Puts a tenant on a plan (<c>InternalRoutes.TenantPlan</c>). Replaces the plan and its overrides
/// whole: overrides left out are cleared. <paramref name="ExpectedRevision"/>, when given, refuses the
/// change (409) if the plan has moved on since the caller read it.
/// </summary>
public record SetTenantPlanRequest(PlanCode PlanCode, PlanLimitOverrides? LimitOverrides = null, int? ExpectedRevision = null);

/// <summary>A tenant's plan as it stands.</summary>
public record TenantPlanResponse(Guid TenantRefId, PlanCode PlanCode, PlanLimitOverrides? LimitOverrides, int PlanRevision, DateTimeOffset? PlanChangedAt);

/// <summary>A workspace and its tenant's plan, for the main database's copy (<c>InternalRoutes.WorkspaceAccounts</c>).</summary>
public record WorkspaceAccountResponse(int WorkspaceId, int TenantId, Guid TenantRefId, PlanCode PlanCode, PlanLimitOverrides? LimitOverrides, int PlanRevision);
