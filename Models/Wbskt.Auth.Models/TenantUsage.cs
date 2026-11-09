using Wbskt.Models.Billing;

namespace Wbskt.Auth.Models;

/// <summary>
/// The tenant's plan, everything it allows, and the seats the auth host counts: workspaces and members.
/// Credits, clients and runs are counted in the management host (<c>GET …/workspaces/{workspaceRef}/usage</c>).
/// </summary>
public sealed record TenantUsageResponse(PlanCode Plan, PlanLimits Limits, UsageCount Workspaces, UsageCount Members);
