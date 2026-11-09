using Wbskt.Models.Billing;

namespace Wbskt.Management.Models;

/// <summary>
/// What a workspace's plan allows and what has been used of it this calendar month (UTC): pooled across
/// the account's workspaces (<c>Account</c>), and this workspace's share of it (<c>Workspace</c>), with
/// the workspace's credits per day over the requested range (<c>Days</c>).
/// </summary>
/// <param name="PeriodStart">The start of the month the credits are counted in.</param>
/// <param name="PeriodEnd">When the month's credits start again.</param>
public sealed record WorkspaceUsageResponse(
    PlanCode Plan,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    AccountUsage Account,
    WorkspaceUsageShare Workspace,
    IReadOnlyCollection<DailyCreditUsage> Days);

/// <summary>Used against the plan's limit, across every workspace of the account.</summary>
public sealed record AccountUsage(CreditUsageAmount Credits, MessageUsageAmount Messages, UsageCount Clients, UsageCount ActiveRuns);

/// <param name="Used">Workflow credits spent this month.</param>
/// <param name="Limit">The plan's monthly credits.</param>
/// <param name="TopUp">
/// Credits the account bought on top, still unspent and unexpired. A run is refused once
/// <c>Used</c> reaches <c>Limit</c> plus <c>TopUp</c>; what a month spends past <c>Limit</c> comes out of
/// <c>TopUp</c> when the month closes.
/// </param>
/// <param name="TopUpExpiresAt">When the soonest-expiring part of <c>TopUp</c> runs out; null with no top-up.</param>
/// <param name="TopUpExpiring">How many of <c>TopUp</c>'s credits run out at <c>TopUpExpiresAt</c>.</param>
public sealed record CreditUsageAmount(decimal Used, decimal Limit, decimal TopUp, DateTimeOffset? TopUpExpiresAt, decimal TopUpExpiring);

/// <summary>Client messages both ways (sent by clients, and commands delivered to them) against the plan's monthly allowance.</summary>
/// <param name="Throttled">
/// True when each of the account's clients may send only one message a minute until the month ends: Free
/// once it has used its allowance, Maker, Pro and Team at twice it. Business is never throttled. Paid plans are
/// never charged for what they use past the limit.
/// </param>
public sealed record MessageUsageAmount(long Used, long Limit, bool Throttled);

/// <param name="Credits">Workflow credits this workspace spent this month.</param>
/// <param name="MonthlyCreditCap">The workspace's own credit cap; null for none.</param>
/// <param name="CreditsReserved">
/// Credits the workspace's runs going still hold of their budgets and may yet spend. A run is refused
/// once <c>Credits</c> plus <c>CreditsReserved</c> reaches <c>MonthlyCreditCap</c>.
/// </param>
/// <param name="Messages">Client messages this workspace carried this month, both ways.</param>
/// <param name="Clients">Registered clients in this workspace.</param>
/// <param name="ActiveRuns">Runs going in this workspace now.</param>
public sealed record WorkspaceUsageShare(decimal Credits, decimal? MonthlyCreditCap, long Messages, int Clients, int ActiveRuns, decimal CreditsReserved);

/// <summary>Workflow credits the workspace spent on one UTC day.</summary>
public sealed record DailyCreditUsage(DateOnly Day, decimal Credits);
