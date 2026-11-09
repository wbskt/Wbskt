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
/// change (409) if the plan has moved on since the caller read it. An operator's plan holds until
/// <paramref name="EndsAt"/> (never, when left out); the tenant then goes back to its subscription's plan,
/// or to Free. Setting an end date that has passed hands it back at the next review.
/// </summary>
public record SetTenantPlanRequest(PlanCode PlanCode, PlanLimitOverrides? LimitOverrides = null, int? ExpectedRevision = null, DateTimeOffset? EndsAt = null);

/// <summary>A tenant's plan as it stands: who set it, and when an operator's plan ends.</summary>
public record TenantPlanResponse(
    Guid TenantRefId,
    PlanCode PlanCode,
    PlanLimitOverrides? LimitOverrides,
    int PlanRevision,
    DateTimeOffset? PlanChangedAt,
    PlanSource Source = PlanSource.Default,
    DateTimeOffset? EndsAt = null);

/// <summary>A workspace and its tenant's plan, for the main database's copy (<c>InternalRoutes.WorkspaceAccounts</c>).</summary>
public record WorkspaceAccountResponse(int WorkspaceId, int TenantId, Guid TenantRefId, PlanCode PlanCode, PlanLimitOverrides? LimitOverrides, int PlanRevision);

/// <summary>
/// A credit pack bought, as the auth database holds it (<c>InternalRoutes.CreditPurchases</c>): what the
/// main database's grant for <paramref name="Source"/> and <paramref name="SourceRef"/> must match.
/// </summary>
/// <param name="HoldRevision">Raised by every hold and release; 0 when the pack was never held.</param>
public record CreditPurchaseResponse(
    int Id,
    int TenantId,
    Guid TenantRefId,
    string Source,
    string SourceRef,
    decimal Credits,
    DateTimeOffset PurchasedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? RefundedAt,
    bool Held,
    int HoldRevision);
