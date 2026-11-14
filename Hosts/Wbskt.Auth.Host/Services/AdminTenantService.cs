using Wbskt.Auth.Models;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.StaffActions;
using Wbskt.Models.Admin;
using Wbskt.Models.Billing;

namespace Wbskt.Auth.Host.Services;

/// <remarks>
/// Every change goes through <see cref="ITenantPlanService.SetAsync"/>, the method billing and the
/// operator route already use, so a staff change takes the same validation, revision check, outbox
/// event and emails. It opens its connections one after another, as the staff runner needs.
/// </remarks>
internal sealed class AdminTenantService(ITenantPlanService plans, ITenantLimits limits, IStaffActionRunner runner, TimeProvider time)
    : IAdminTenantService
{
    public const string SetPlanAction = "SetTenantPlan";
    public const string SetLimitsAction = "SetTenantLimits";
    public const string TargetKind = "Account";

    private static readonly TimeSpan LongestStaffPlan = TimeSpan.FromDays(366);

    public async Task<Result<AdminTenantLimitsResponse>> GetLimitsAsync(Guid tenantRef, CancellationToken cancellationToken)
    {
        var plan = await plans.GetAsync(tenantRef, cancellationToken);
        if (plan.IsFailure)
        {
            return plan.Error;
        }

        return new AdminTenantLimitsResponse(plan.Value, await limits.ForTenantAsync(tenantRef, cancellationToken));
    }

    public Task<Result<StaffActionResponse>> SetPlanAsync(Guid tenantRef, AdminSetTenantPlanRequest request, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        if (request.EndsAt <= now || request.EndsAt > now + LongestStaffPlan)
        {
            return Task.FromResult<Result<StaffActionResponse>>(
                Error.Validation("PLAN_ENDS_AT_INVALID", "A staff plan ends in the future and within a year."));
        }

        var set = new SetTenantPlanRequest(request.PlanCode, request.LimitOverrides, request.ExpectedRevision, request.EndsAt, request.ContractClients);
        return runner.RunAsync(
            request.Staff,
            new StaffAction(SetPlanAction, TargetKind, tenantRef),
            async ct => await plans.SetAsync(tenantRef, set, PlanSource.Operator, ct),
            cancellationToken);
    }

    public async Task<Result<StaffActionResponse>> SetLimitsAsync(Guid tenantRef, AdminSetTenantLimitsRequest request, CancellationToken cancellationToken)
    {
        var current = await plans.GetAsync(tenantRef, cancellationToken);
        if (current.IsFailure)
        {
            return current.Error;
        }

        // The plan, its source and its end date are carried over as they are; the revision check refuses
        // the change if any of them moved since the staff member read them.
        var plan = current.Value;
        var set = new SetTenantPlanRequest(plan.PlanCode, request.LimitOverrides, request.ExpectedRevision, plan.EndsAt);
        return await runner.RunAsync(
            request.Staff,
            new StaffAction(SetLimitsAction, TargetKind, tenantRef),
            async ct => await plans.SetAsync(tenantRef, set, plan.Source, ct),
            cancellationToken);
    }
}
