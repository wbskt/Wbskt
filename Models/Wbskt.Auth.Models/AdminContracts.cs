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
/// tenant's whole set, and <paramref name="ExpectedRevision"/> is the revision the staff member saw.
/// </summary>
public sealed record AdminSetTenantPlanRequest(
    StaffChange Staff,
    PlanCode PlanCode,
    PlanLimitOverrides? LimitOverrides,
    int ExpectedRevision,
    DateTimeOffset EndsAt,
    int? ContractClients = null);

/// <summary>
/// Replaces a tenant's limit overrides and nothing else (<c>InternalRoutes.AdminTenantLimits</c>): its
/// plan, and whoever set it, stay in charge, so a paying tenant's subscription still moves its plan.
/// Null or empty overrides clear them.
/// </summary>
public sealed record AdminSetTenantLimitsRequest(StaffChange Staff, PlanLimitOverrides? LimitOverrides, int ExpectedRevision);
