namespace Wbskt.Models.Billing;

/// <summary>
/// Limits that replace a plan's own, one field at a time: a field left null keeps the plan's value.
/// Used twice, in the same shape: by operators in configuration, to reprice a plan for everyone
/// (<c>Billing:Plans:{PlanCode}</c>), and on one tenant, for a negotiated Business contract.
/// </summary>
public sealed record PlanLimitOverrides
{
    public int? Clients { get; init; }
    public long? MessagesPerMonth { get; init; }
    public decimal? CreditsPerMonth { get; init; }
    public int? ActiveRuns { get; init; }
    public int? Workspaces { get; init; }
    public int? Members { get; init; }
    public int? RawReadingsDays { get; init; }
    public int? HourlyReadingsDays { get; init; }
    public int? RunHistoryDays { get; init; }

    /// <summary><paramref name="limits"/> with every field this sets replaced.</summary>
    public PlanLimits ApplyTo(PlanLimits limits) => limits with
    {
        Clients = Clients ?? limits.Clients,
        MessagesPerMonth = MessagesPerMonth ?? limits.MessagesPerMonth,
        CreditsPerMonth = CreditsPerMonth ?? limits.CreditsPerMonth,
        ActiveRuns = ActiveRuns ?? limits.ActiveRuns,
        Workspaces = Workspaces ?? limits.Workspaces,
        Members = Members ?? limits.Members,
        RawReadingsDays = RawReadingsDays ?? limits.RawReadingsDays,
        HourlyReadingsDays = HourlyReadingsDays ?? limits.HourlyReadingsDays,
        RunHistoryDays = RunHistoryDays ?? limits.RunHistoryDays
    };

    /// <summary>The names of the fields set below zero, which no limit can be.</summary>
    public IReadOnlyCollection<string> NegativeFields()
    {
        var negative = new List<string>();
        Check(Clients, nameof(Clients));
        Check(MessagesPerMonth, nameof(MessagesPerMonth));
        Check(CreditsPerMonth, nameof(CreditsPerMonth));
        Check(ActiveRuns, nameof(ActiveRuns));
        Check(Workspaces, nameof(Workspaces));
        Check(Members, nameof(Members));
        Check(RawReadingsDays, nameof(RawReadingsDays));
        Check(HourlyReadingsDays, nameof(HourlyReadingsDays));
        Check(RunHistoryDays, nameof(RunHistoryDays));
        return negative;

        void Check(decimal? value, string name)
        {
            if (value < 0)
            {
                negative.Add(name);
            }
        }
    }
}
