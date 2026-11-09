using Microsoft.Extensions.Options;
using Wbskt.Models.Billing;

namespace Wbskt.Infrastructure.Billing;

/// <summary>
/// The limits each plan gives, and the one place they are worked out: the built-in table, then the
/// operator's changes from configuration, then a tenant's own overrides, each replacing the field it
/// sets.
/// </summary>
public sealed class PlanCatalog
{
    /// <summary>
    /// The plans as priced (Docs/Billing.md). Pooled across a tenant's workspaces, so a Maker with three
    /// workspaces has 100 clients between them, not 100 each.
    /// </summary>
    public static readonly IReadOnlyDictionary<PlanCode, PlanLimits> BuiltIn = new Dictionary<PlanCode, PlanLimits>
    {
        [PlanCode.Free] = new(Clients: 20, MessagesPerMonth: 1_500_000, CreditsPerMonth: 2_000m, ActiveRuns: 20, Workspaces: 1, Members: 1, RawReadingsDays: 3, HourlyReadingsDays: 30, RunHistoryDays: 7),
        [PlanCode.Maker] = new(Clients: 100, MessagesPerMonth: 10_000_000, CreditsPerMonth: 25_000m, ActiveRuns: 100, Workspaces: 3, Members: 3, RawReadingsDays: 14, HourlyReadingsDays: 365, RunHistoryDays: 14),
        [PlanCode.Pro] = new(Clients: 500, MessagesPerMonth: 50_000_000, CreditsPerMonth: 150_000m, ActiveRuns: 200, Workspaces: 10, Members: 10, RawReadingsDays: 30, HourlyReadingsDays: 730, RunHistoryDays: 30),
        [PlanCode.Team] = new(Clients: 2_000, MessagesPerMonth: 200_000_000, CreditsPerMonth: 600_000m, ActiveRuns: 200, Workspaces: 25, Members: 25, RawReadingsDays: 30, HourlyReadingsDays: 730, RunHistoryDays: 30),

        // Business's row is its smallest contract; a larger one sets its own limits (BusinessContract).
        [PlanCode.Business] = BusinessContract.LimitsFor(BusinessContract.MinimumClients)
    };

    private readonly IReadOnlyDictionary<PlanCode, PlanLimits> _plans;

    public PlanCatalog(IOptions<PlanCatalogOptions> options)
    {
        Dictionary<PlanCode, PlanLimitOverrides> configured = options.Value.Plans;
        EnsureValid(configured);
        _plans = BuiltIn.ToDictionary(
            plan => plan.Key,
            plan => configured.TryGetValue(plan.Key, out PlanLimitOverrides? changes) ? changes.ApplyTo(plan.Value) : plan.Value);
    }

    /// <summary>What <paramref name="plan"/> allows, with <paramref name="tenantOverrides"/> applied last.</summary>
    public PlanLimits LimitsFor(PlanCode plan, PlanLimitOverrides? tenantOverrides = null)
    {
        PlanLimits limits = _plans.TryGetValue(plan, out PlanLimits? known) ? known : _plans[PlanCode.Free];
        return tenantOverrides?.ApplyTo(limits) ?? limits;
    }

    // A negative limit would read as "already over" everywhere it is compared, so configuration that
    // sets one fails the host at start rather than refusing every client and run.
    private static void EnsureValid(Dictionary<PlanCode, PlanLimitOverrides> configured)
    {
        string[] negative = configured
            .SelectMany(plan => plan.Value.NegativeFields().Select(field => $"{plan.Key}:{field}"))
            .ToArray();
        if (negative.Length > 0)
        {
            throw new InvalidOperationException($"{PlanCatalogOptions.SectionName} has negative limits: {string.Join(", ", negative)}.");
        }
    }
}
