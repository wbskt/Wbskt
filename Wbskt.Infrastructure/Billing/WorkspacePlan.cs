using Wbskt.Models.Billing;

namespace Wbskt.Infrastructure.Billing;

/// <summary>The plan a workspace is under and what it allows, as <see cref="IWorkspacePlans"/> resolves it.</summary>
/// <param name="TenantId">The tenant whose limits the workspace shares; null while no copy of its account has arrived.</param>
public sealed record WorkspacePlan(int WorkspaceId, int? TenantId, PlanCode PlanCode, PlanLimits Limits);
