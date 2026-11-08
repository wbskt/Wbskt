namespace Wbskt.Models.Billing;

/// <summary>
/// What a plan allows. Every limit is pooled across the tenant's workspaces unless its name says
/// otherwise; a workspace may hold a smaller share of the credits (a workspace credit cap).
/// </summary>
/// <param name="Clients">Registered clients across the tenant.</param>
/// <param name="MessagesPerMonth">Client messages, both ways, in a calendar month (UTC).</param>
/// <param name="CreditsPerMonth">Workflow credits in a calendar month (UTC).</param>
/// <param name="ActiveRuns">Workflow runs going at once across the tenant.</param>
/// <param name="Workspaces">Workspaces in the tenant.</param>
/// <param name="Members">People in the tenant.</param>
/// <param name="RawReadingsDays">How long each client reading is kept as sent.</param>
/// <param name="HourlyReadingsDays">How long the hourly roll-up of readings is kept.</param>
/// <param name="RunHistoryDays">How long a finished run's history is kept.</param>
public sealed record PlanLimits(
    int Clients,
    long MessagesPerMonth,
    decimal CreditsPerMonth,
    int ActiveRuns,
    int Workspaces,
    int Members,
    int RawReadingsDays,
    int HourlyReadingsDays,
    int RunHistoryDays);
