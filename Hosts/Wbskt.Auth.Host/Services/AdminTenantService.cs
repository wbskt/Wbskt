using Wbskt.Auth.Host.Models;
using Wbskt.Auth.Host.Providers;
using Wbskt.Auth.Models;
using Wbskt.Events.Billing;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Events.Outbox;
using Wbskt.Infrastructure.StaffActions;
using Wbskt.Models;
using Wbskt.Models.Admin;
using Wbskt.Models.Billing;

namespace Wbskt.Auth.Host.Services;

/// <remarks>
/// Every change goes through <see cref="ITenantPlanService.SetAsync"/>, the method billing and the
/// operator route already use, so a staff change takes the same validation, revision check, outbox
/// event and emails. A credit grant is only its event: the auth database's outbox carries it to the main
/// database, as a bought credit pack's is. Every change opens its connections one after another, as the
/// staff runner needs.
/// </remarks>
internal sealed class AdminTenantService(
    ITenantPlanService plans,
    ITenantLimits limits,
    IStaffActionRunner runner,
    TimeProvider time,
    ITenantPlanProvider tenants,
    IAuditWorkspaces audit,
    IOutboxWriter outbox,
    ITenantPlanReview review,
    ILogger<AdminTenantService> logger)
    : IAdminTenantService
{
    public const string SetPlanAction = "SetTenantPlan";
    public const string SetLimitsAction = "SetTenantLimits";
    public const string EndPlanAction = "EndStaffPlan";
    public const string GrantCreditsAction = "GrantCredits";
    public const string RevokeCreditsAction = "RevokeCreditGrant";
    public const string TargetKind = "Account";

    private static readonly TimeSpan LongestStaffPlan = TimeSpan.FromDays(366);
    private static readonly TimeSpan LongestStaffGrant = TimeSpan.FromDays(366);
    private static readonly Error TenantNotFound = Error.NotFound("TENANT_NOT_FOUND", "No tenant has that RefId.");

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

    public async Task<Result<StaffActionResponse>> EndStaffPlanAsync(Guid tenantRef, AdminEndStaffPlanRequest request, CancellationToken cancellationToken)
    {
        int? tenantId = null;
        var result = await runner.RunAsync(
            request.Staff,
            new StaffAction(EndPlanAction, TargetKind, tenantRef),
            async ct =>
            {
                TenantPlan? current = await tenants.FindByRefIdAsync(tenantRef, ct);
                if (current is null)
                {
                    return Result.Failure(TenantNotFound);
                }

                DateTime now = time.GetUtcNow().UtcDateTime;
                if (!current.IsOperatorPlanAt(now))
                {
                    return Result.Failure(Error.Conflict("STAFF_PLAN_NOT_ACTIVE", "The tenant is not on a staff plan; billing already decides its plan."));
                }

                // Ending it is setting its end to now: from then on billing moves the tenant, as it would at the end date.
                tenantId = current.TenantId;
                var end = new SetTenantPlanRequest(current.PlanCode, current.LimitOverrides, request.ExpectedRevision, now.ToUtcOffset());
                return await plans.SetAsync(tenantRef, end, PlanSource.Operator, ct);
            },
            cancellationToken);

        if (result.IsSuccess && tenantId.HasValue)
        {
            await ReviewNowAsync(tenantId.Value, cancellationToken);
        }

        return result;
    }

    public Task<Result<StaffActionResponse>> GrantCreditsAsync(Guid tenantRef, AdminGrantCreditsRequest request, CancellationToken cancellationToken)
    {
        if (request.Credits <= 0 || request.Credits > StaffCreditGrants.MaxCredits || decimal.Round(request.Credits, 4) != request.Credits)
        {
            return Task.FromResult<Result<StaffActionResponse>>(
                Error.Validation("CREDITS_INVALID", $"Give more than 0 and at most {StaffCreditGrants.MaxCredits:0} credits, to 4 decimal places."));
        }

        var now = time.GetUtcNow();
        if (request.ExpiresAt <= now || request.ExpiresAt > now + LongestStaffGrant)
        {
            return Task.FromResult<Result<StaffActionResponse>>(
                Error.Validation("CREDITS_EXPIRES_AT_INVALID", "Staff credits expire in the future and within a year."));
        }

        return runner.RunAsync(
            request.Staff,
            new StaffAction(GrantCreditsAction, TargetKind, tenantRef),
            async ct =>
            {
                TenantPlan? tenant = await tenants.FindByRefIdAsync(tenantRef, ct);
                if (tenant is null)
                {
                    return Result.Failure(TenantNotFound);
                }

                // The key is the grant's reference, so the main database records it once however often it is delivered.
                var sourceRef = StaffCreditGrants.SourceRef(tenant.TenantRefId, request.Staff.IdempotencyKey);
                var granted = new TenantCreditsGrantedEvent(tenant.TenantId, tenant.TenantRefId, request.Credits, request.ExpiresAt, StaffCreditGrants.Source, sourceRef)
                {
                    WorkspaceIds = await audit.OfTenantAsync(tenant.TenantId, ct)
                };
                await outbox.AddAsync(OutboxMessage.Create(granted), ct);
                return Result.Success();
            },
            cancellationToken);
    }

    public Task<Result<StaffActionResponse>> RevokeCreditGrantAsync(Guid tenantRef, Guid grantRef, AdminRevokeCreditGrantRequest request, CancellationToken cancellationToken)
    {
        return runner.RunAsync(
            request.Staff,
            new StaffAction(RevokeCreditsAction, TargetKind, tenantRef),
            async ct =>
            {
                TenantPlan? tenant = await tenants.FindByRefIdAsync(tenantRef, ct);
                if (tenant is null)
                {
                    return Result.Failure(TenantNotFound);
                }

                // Only this tenant's staff grant can be named here: the source is fixed and the reference starts with
                // the tenant, so neither a bought pack nor another tenant's grant is ever touched.
                var sourceRef = StaffCreditGrants.SourceRef(tenant.TenantRefId, grantRef);
                var revoked = new TenantCreditGrantRevokedEvent(tenant.TenantId, tenant.TenantRefId, StaffCreditGrants.Source, sourceRef)
                {
                    WorkspaceIds = await audit.OfTenantAsync(tenant.TenantId, ct)
                };
                await outbox.AddAsync(OutboxMessage.Create(revoked), ct);
                return Result.Success();
            },
            cancellationToken);
    }

    /// <summary>
    /// Moves the tenant to its subscription's plan now rather than at the next hourly review. It runs after
    /// the change has committed and asks the payment provider, so a failure is only logged: the hourly review
    /// picks the tenant up, since its staff plan has ended.
    /// </summary>
    private async Task ReviewNowAsync(int tenantId, CancellationToken cancellationToken)
    {
        try
        {
            await review.ReviewAsync(tenantId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Ended tenant {TenantId}'s staff plan; billing will move it at the next hourly review.", tenantId);
        }
    }
}
