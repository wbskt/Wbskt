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
public sealed record AccountUsage(CreditUsageAmount Credits, UsageCount Clients, UsageCount ActiveRuns);

public sealed record CreditUsageAmount(decimal Used, decimal Limit);

/// <param name="Credits">Workflow credits this workspace spent this month.</param>
/// <param name="MonthlyCreditCap">The workspace's own credit cap; null for none.</param>
/// <param name="Clients">Registered clients in this workspace.</param>
/// <param name="ActiveRuns">Runs going in this workspace now.</param>
public sealed record WorkspaceUsageShare(decimal Credits, decimal? MonthlyCreditCap, int Clients, int ActiveRuns);

/// <summary>Workflow credits the workspace spent on one UTC day.</summary>
public sealed record DailyCreditUsage(DateOnly Day, decimal Credits);
