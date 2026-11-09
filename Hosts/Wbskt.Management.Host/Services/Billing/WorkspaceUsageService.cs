using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Billing;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Providers;
using Wbskt.Management.Models;
using Wbskt.Models.Billing;

namespace Wbskt.Management.Host.Services.Billing;

internal sealed class WorkspaceUsageService(IWorkspaceUsageProvider usage, IWorkspacePlans plans, TimeProvider clock) : IWorkspaceUsageService
{
    private static readonly TimeSpan DefaultRange = TimeSpan.FromDays(31);
    private static readonly TimeSpan MaxRange = TimeSpan.FromDays(366);

    public async Task<Result<WorkspaceUsageResponse>> GetAsync(int workspaceId, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct)
    {
        DateTimeOffset now = clock.GetUtcNow();
        Result<TimeRange> range = TimeRange.Resolve(from, to, now, DefaultRange, MaxRange);
        if (range.IsFailure)
        {
            return Result<WorkspaceUsageResponse>.Failure(range.Error);
        }

        var monthStart = new DateOnly(now.UtcDateTime.Year, now.UtcDateTime.Month, 1);
        WorkspacePlan plan = await plans.GetAsync(workspaceId, ct);
        WorkspaceUsageSummary summary = await usage.GetSummaryAsync(workspaceId, plan.TenantId, monthStart, ct);

        // The range is half-open, so a `to` at midnight does not take in the day it starts.
        IReadOnlyCollection<DailyCreditUsage> days = await usage.GetDailyCreditsAsync(
            workspaceId,
            DateOnly.FromDateTime(range.Value.FromUtc),
            DateOnly.FromDateTime(range.Value.ToUtc.AddTicks(-1)),
            ct);

        DateTime periodStart = monthStart.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        return Result<WorkspaceUsageResponse>.Success(new WorkspaceUsageResponse(
            plan.PlanCode,
            new DateTimeOffset(periodStart),
            new DateTimeOffset(periodStart.AddMonths(1)),
            new AccountUsage(
                new CreditUsageAmount(summary.TenantCredits, plan.Limits.CreditsPerMonth),
                new UsageCount(summary.TenantClients, plan.Limits.Clients),
                new UsageCount(summary.TenantActiveRuns, plan.Limits.ActiveRuns)),
            new WorkspaceUsageShare(summary.WorkspaceCredits, summary.MonthlyCreditCap, summary.WorkspaceClients, summary.WorkspaceActiveRuns),
            days));
    }
}
