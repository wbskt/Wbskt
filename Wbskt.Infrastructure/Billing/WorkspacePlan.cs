using Wbskt.Models.Billing;

namespace Wbskt.Infrastructure.Billing;

/// <summary>The plan a workspace is under and what it allows, as <see cref="IWorkspacePlans"/> resolves it.</summary>
/// <param name="TenantId">The tenant whose limits the workspace shares; null while no copy of its account has arrived.</param>
/// <param name="Frozen">Wbskt support has frozen the account: nothing runs and no client enrols.</param>
public sealed record WorkspacePlan(int WorkspaceId, int? TenantId, PlanCode PlanCode, PlanLimits Limits, bool Frozen = false)
{
    /// <summary>Registered clients, pooled across the tenant's workspaces.</summary>
    public PlanCeiling ClientCeiling => new(Limits.Clients, TenantId);

    /// <summary>Runs going at once, pooled across the tenant's workspaces.</summary>
    public PlanCeiling ActiveRunCeiling => new(Limits.ActiveRuns, TenantId);

    /// <summary>Workflow credits a calendar month, pooled across the tenant's workspaces.</summary>
    public PlanCredits Credits => new(Limits.CreditsPerMonth, TenantId)
    {
        RunsRefusedAt = PlanCode == PlanCode.Business ? Limits.CreditsPerMonth * BusinessContract.CreditOverageMultiple : Limits.CreditsPerMonth
    };
}
