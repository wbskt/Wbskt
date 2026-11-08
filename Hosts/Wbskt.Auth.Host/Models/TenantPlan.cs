using Wbskt.Models.Billing;

namespace Wbskt.Auth.Host.Models;

/// <summary>A tenant's plan as <c>dbo.Tenants</c> holds it.</summary>
public sealed record TenantPlan(int TenantId, Guid TenantRefId, PlanCode PlanCode, PlanLimitOverrides? LimitOverrides, int PlanRevision, DateTime? PlanChangedAt);
