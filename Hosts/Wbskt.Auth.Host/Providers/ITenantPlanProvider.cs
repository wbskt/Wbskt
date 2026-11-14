using Wbskt.Auth.Host.Models;
using Wbskt.Auth.Models;
using Wbskt.Infrastructure.Events.Outbox;
using Wbskt.Models.Billing;

namespace Wbskt.Auth.Host.Providers;

/// <summary>The plan each tenant is on, and each workspace with its tenant's plan.</summary>
internal interface ITenantPlanProvider
{
    Task<TenantPlan?> FindByRefIdAsync(Guid tenantRef, CancellationToken cancellationToken);

    Task<TenantPlan?> FindByIdAsync(int tenantId, CancellationToken cancellationToken);

    /// <summary>The tenant's workspaces and members, as its plan's limits count them.</summary>
    Task<(int Workspaces, int Members)> GetSeatsAsync(int tenantId, CancellationToken cancellationToken);

    /// <summary>
    /// Puts the tenant on <paramref name="planCode"/> and queues <paramref name="changedEvent"/> in the
    /// same transaction, if its plan is still at <paramref name="expectedRevision"/>, recording who set it
    /// and when an operator's plan ends. Null when it was not: someone else changed it first.
    /// </summary>
    Task<TenantPlan?> SetAsync(int tenantId, PlanCode planCode, PlanLimitOverrides? limitOverrides, int expectedRevision, PlanSource source, DateTime? endsAt, OutboxMessage changedEvent, CancellationToken cancellationToken);

    /// <summary>Up to <paramref name="take"/> workspaces with their tenant's plan, after <paramref name="afterWorkspaceId"/>, by workspace id.</summary>
    Task<IReadOnlyCollection<WorkspaceAccountResponse>> GetWorkspaceAccountsAsync(int? afterWorkspaceId, int take, int? tenantId, int? workspaceId, CancellationToken cancellationToken);

    Task<WorkspaceAccountResponse?> FindWorkspaceAccountByRefIdAsync(Guid workspaceRef, CancellationToken cancellationToken);
}
