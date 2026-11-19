using System.ComponentModel.DataAnnotations;
using Wbskt.Models.Admin;
using Wbskt.Models.Billing;

namespace Wbskt.Auth.Models;

/// <summary>
/// A tenant's plan as stored, and the limits it runs under now with its overrides applied, for the
/// platform admin host (<c>InternalRoutes.AdminTenantLimits</c>).
/// </summary>
public sealed record AdminTenantLimitsResponse(TenantPlanResponse Plan, PlanLimits Effective);

/// <summary>
/// Puts a tenant on a staff plan (<c>InternalRoutes.AdminTenantPlan</c>). Billing leaves the tenant alone
/// until <paramref name="EndsAt"/>, so a staff plan always ends, within a year. The overrides replace the
/// tenant's whole set, and <paramref name="ExpectedRevision"/> is the revision the staff member saw. A tenant
/// with a subscription still billing is refused unless <paramref name="KeepSubscription"/> is set, as for
/// <see cref="SetTenantPlanRequest"/>.
/// </summary>
public sealed record AdminSetTenantPlanRequest(
    StaffChange Staff,
    PlanCode PlanCode,
    PlanLimitOverrides? LimitOverrides,
    int ExpectedRevision,
    DateTimeOffset EndsAt,
    int? ContractClients = null,
    bool KeepSubscription = false) : IStaffRequest;

/// <summary>
/// Replaces a tenant's limit overrides and nothing else (<c>InternalRoutes.AdminTenantLimits</c>): its
/// plan, and whoever set it, stay in charge, so a paying tenant's subscription still moves its plan.
/// Null or empty overrides clear them.
/// </summary>
public sealed record AdminSetTenantLimitsRequest(StaffChange Staff, PlanLimitOverrides? LimitOverrides, int ExpectedRevision) : IStaffRequest;

/// <summary>
/// Ends the tenant's staff plan now (<c>InternalRoutes.AdminTenantPlanEnd</c>) and hands it back to billing,
/// which puts it on its subscription's plan, or Free. <paramref name="ExpectedRevision"/> is the revision seen.
/// </summary>
public sealed record AdminEndStaffPlanRequest(StaffChange Staff, int ExpectedRevision) : IStaffRequest;

/// <summary>
/// Gives the tenant <paramref name="Credits"/> workflow credits, spendable until <paramref name="ExpiresAt"/>
/// (<c>InternalRoutes.AdminTenantCreditGrants</c>). The grant's reference is the staff change's idempotency key.
/// </summary>
public sealed record AdminGrantCreditsRequest(StaffChange Staff, decimal Credits, DateTimeOffset ExpiresAt) : IStaffRequest;

/// <summary>Takes back what is left of a grant staff gave (<c>InternalRoutes.AdminTenantCreditGrantRevoke</c>).</summary>
public sealed record AdminRevokeCreditGrantRequest(StaffChange Staff) : IStaffRequest;

/// <summary>
/// A staff change to a person's account that takes nothing but the change itself: clearing a sign-in
/// lockout, resending the email verification, sending a password reset, or signing out everywhere
/// (<c>InternalRoutes.AdminUser*</c>).
/// </summary>
public sealed record AdminUserActionRequest(StaffChange Staff) : IStaffRequest;

/// <summary>
/// Turns a person's sign-in on or off (<c>InternalRoutes.AdminUserSignIn</c>). Off also ends every session
/// they have.
/// </summary>
public sealed record AdminSetUserSignInRequest(StaffChange Staff, bool Allowed) : IStaffRequest;

/// <summary>
/// Corrects a person's address (<c>InternalRoutes.AdminUserEmail</c>) when they mistyped it at sign-up and
/// never verified it, so the verification link can reach them. The new address starts unverified and is
/// mailed a link. A verified address is refused: only its owner can move it, through the console.
/// </summary>
public sealed record AdminCorrectUserEmailRequest(
    StaffChange Staff,
    [Required, EmailAddress, StringLength(100)] string Email) : IStaffRequest;

/// <summary>
/// Suspends a member in one tenant, or lifts it (<c>InternalRoutes.AdminTenantMemberSuspension</c>). Their
/// account and other tenants are untouched. Refused when it would leave the tenant without an administrator.
/// </summary>
public sealed record AdminSetMemberSuspendedRequest(StaffChange Staff, bool Suspended) : IStaffRequest;

/// <summary>
/// Removes a member from a tenant (<c>InternalRoutes.AdminTenantMemberRemove</c>), as leaving would: their
/// roles there go, and workspaces they owned pass to another of its administrators.
/// </summary>
public sealed record AdminRemoveMemberRequest(StaffChange Staff) : IStaffRequest;

/// <summary>
/// Makes a member an administrator of the tenant (<c>InternalRoutes.AdminTenantMemberRepairAdministrator</c>):
/// its Admin role tenant-wide, and their own tenant-wide denies of users.manage and roles.manage removed.
/// </summary>
public sealed record AdminRepairAdministratorRequest(StaffChange Staff) : IStaffRequest;

/// <summary>
/// Freezes a tenant, or lifts the freeze (<c>InternalRoutes.AdminTenantFreeze</c>). While frozen its
/// members can sign in and read, no workspace in it takes a change, and nothing runs; a freeze cancels
/// its running workflow runs and revokes its clients, which stay revoked after the freeze is lifted.
/// </summary>
public sealed record AdminSetTenantFrozenRequest(StaffChange Staff, bool Frozen) : IStaffRequest;
