using Wbskt.Models.Billing;

namespace Wbskt.Auth.Models;

/// <summary>
/// A tenant's plan as stored, and the limits it runs under now with its overrides applied, for the
/// platform admin host (<c>InternalRoutes.AdminTenantLimits</c>).
/// </summary>
public sealed record AdminTenantLimitsResponse(TenantPlanResponse Plan, PlanLimits Effective);
