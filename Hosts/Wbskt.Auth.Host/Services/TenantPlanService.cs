using Wbskt.Auth.Host.Models;
using Wbskt.Auth.Host.Providers;
using Wbskt.Auth.Models;
using Wbskt.Events.Auth;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Events.Outbox;
using Wbskt.Models;
using Wbskt.Models.Billing;

namespace Wbskt.Auth.Host.Services;

internal sealed class TenantPlanService(ITenantPlanProvider plans, IAuditWorkspaces audit, ILogger<TenantPlanService> logger) : ITenantPlanService
{
    private static readonly Error NotFound = Error.NotFound("TENANT_NOT_FOUND", "Tenant not found.");
    private static readonly Error Changed = Error.Conflict("TENANT_PLAN_CHANGED", "The tenant's plan changed since it was read. Read it again and retry.");

    public async Task<Result<TenantPlanResponse>> GetAsync(Guid tenantRef, CancellationToken cancellationToken)
    {
        TenantPlan? plan = await plans.FindByRefIdAsync(tenantRef, cancellationToken);
        return plan is null ? NotFound : ToResponse(plan);
    }

    public async Task<Result<TenantPlanResponse>> SetAsync(Guid tenantRef, SetTenantPlanRequest request, PlanSource source, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(request.PlanCode))
        {
            return Error.Validation("PLAN_CODE_INVALID", $"'planCode' must be one of {string.Join(", ", Enum.GetNames<PlanCode>())}.");
        }

        IReadOnlyCollection<string> negative = request.LimitOverrides?.NegativeFields() ?? [];
        if (negative.Count > 0)
        {
            return Error.Validation("PLAN_LIMIT_INVALID", $"Limits cannot be negative: {string.Join(", ", negative)}.");
        }

        TenantPlan? current = await plans.FindByRefIdAsync(tenantRef, cancellationToken);
        if (current is null)
        {
            return NotFound;
        }

        if (request.ExpectedRevision is { } expected && expected != current.PlanRevision)
        {
            return Changed;
        }

        // An empty set of overrides is stored as none, so it compares equal to none here too.
        PlanLimitOverrides? overrides = request.LimitOverrides == new PlanLimitOverrides() ? null : request.LimitOverrides;
        DateTime? endsAt = source == PlanSource.Operator ? request.EndsAt?.UtcDateTime : null;
        if (current.PlanCode == request.PlanCode && current.LimitOverrides == overrides && current.Source == source && current.EndsAt == endsAt)
        {
            return ToResponse(current);
        }

        var changedEvent = new TenantPlanChangedEvent(current.TenantId, current.TenantRefId, request.PlanCode, current.PlanCode, current.PlanRevision + 1)
        {
            WorkspaceIds = await audit.OfTenantAsync(current.TenantId, cancellationToken)
        };

        TenantPlan? updated = await plans.SetAsync(current.TenantId, request.PlanCode, overrides, current.PlanRevision, source, endsAt, OutboxMessage.Create(changedEvent), cancellationToken);
        if (updated is null)
        {
            return Changed;
        }

        logger.LogInformation(
            "Tenant {TenantId} moved from {PreviousPlan} to {Plan} ({Source}) at revision {Revision}",
            updated.TenantId, current.PlanCode, updated.PlanCode, updated.Source, updated.PlanRevision);
        return ToResponse(updated);
    }

    public async Task<Result<Page<WorkspaceAccountResponse>>> GetWorkspaceAccountsAsync(PageRequest page, int? tenantId, int? workspaceId, CancellationToken cancellationToken)
    {
        Result<long?> after = page.AfterKey();
        if (after.IsFailure)
        {
            return after.Error;
        }

        if (after.Value is > int.MaxValue or < 0)
        {
            return PageRequest.CursorInvalid;
        }

        int take = page.LimitOr(Paging.MaxPageSize);
        IReadOnlyCollection<WorkspaceAccountResponse> rows = await plans.GetWorkspaceAccountsAsync((int?)after.Value, take, tenantId, workspaceId, cancellationToken);
        return new Page<WorkspaceAccountResponse>
        {
            Items = rows,
            NextCursor = rows.Count == take ? PageRequest.KeyCursor(rows.Last().WorkspaceId) : null
        };
    }

    private static TenantPlanResponse ToResponse(TenantPlan plan) =>
        new(plan.TenantRefId, plan.PlanCode, plan.LimitOverrides, plan.PlanRevision, plan.PlanChangedAt?.ToUtcOffset(), plan.Source, plan.EndsAt?.ToUtcOffset());
}
