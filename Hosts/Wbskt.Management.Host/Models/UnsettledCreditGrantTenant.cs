using Wbskt.Models.Billing;

namespace Wbskt.Management.Host.Models;

/// <summary>A row of <c>dbo.TenantCreditGrants_GetUnsettled</c>: a tenant whose month still has to be drawn from its grants, and its plan copy.</summary>
public sealed record UnsettledCreditGrantTenant(int TenantId, PlanCode PlanCode, PlanLimitOverrides? LimitOverrides);
