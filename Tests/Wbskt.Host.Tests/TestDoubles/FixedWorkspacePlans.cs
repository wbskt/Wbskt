using Wbskt.Infrastructure.Billing;
using Wbskt.Models.Billing;

namespace Wbskt.Host.Tests;

/// <summary>Every workspace on one plan, for tests that are not about how the plan is resolved.</summary>
internal sealed class FixedWorkspacePlans : IWorkspacePlans
{
    private readonly PlanCode _plan;
    private readonly int? _tenantId;
    private readonly PlanLimits _limits;

    public FixedWorkspacePlans(PlanCode plan = PlanCode.Free, int? tenantId = null, Func<PlanLimits, PlanLimits>? adjust = null)
    {
        _plan = plan;
        _tenantId = tenantId;
        PlanLimits limits = PlanCatalog.BuiltIn[plan];
        _limits = adjust is null ? limits : adjust(limits);
    }

    public PlanLimits Limits => _limits;

    public Task<WorkspacePlan> GetAsync(int workspaceId, CancellationToken ct) =>
        Task.FromResult(new WorkspacePlan(workspaceId, _tenantId, _plan, _limits));

    public void Invalidate(int workspaceId)
    {
    }
}
