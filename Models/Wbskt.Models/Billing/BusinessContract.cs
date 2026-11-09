namespace Wbskt.Models.Billing;

/// <summary>
/// How a Business contract's limits follow from the clients it is priced on (Docs/Billing.md, Plans):
/// $0.15 a client a month, at least <see cref="MinimumClients"/>, each client bringing
/// <see cref="MessagesPerClient"/> messages and <see cref="CreditsPerClient"/> credits a month. Deriving
/// them keeps a contract from being priced on one client count and sized on another.
/// </summary>
public static class BusinessContract
{
    public const int MinimumClients = 2_000;

    public const long MessagesPerClient = 100_000;

    public const decimal CreditsPerClient = 300m;

    /// <summary>Business's limits for a contract of <paramref name="clients"/>; the plan catalog's row is the smallest.</summary>
    public static PlanLimits LimitsFor(int clients) => new(
        Clients: clients,
        MessagesPerMonth: clients * MessagesPerClient,
        CreditsPerMonth: clients * CreditsPerClient,
        ActiveRuns: 200,
        Workspaces: 50,
        Members: 50,
        RawReadingsDays: 90,
        HourlyReadingsDays: 730,
        RunHistoryDays: 90);

    /// <summary>
    /// The tenant overrides for a contract of <paramref name="clients"/>: its clients, messages and
    /// credits, unless <paramref name="explicitOverrides"/> sets one itself. Every other field is the
    /// explicit one's, so the rest still follows the plan.
    /// </summary>
    public static PlanLimitOverrides OverridesFor(int clients, PlanLimitOverrides? explicitOverrides)
    {
        PlanLimitOverrides given = explicitOverrides ?? new PlanLimitOverrides();
        return given with
        {
            Clients = given.Clients ?? clients,
            MessagesPerMonth = given.MessagesPerMonth ?? clients * MessagesPerClient,
            CreditsPerMonth = given.CreditsPerMonth ?? clients * CreditsPerClient
        };
    }
}
