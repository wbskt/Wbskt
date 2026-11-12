namespace Wbskt.Management.Models;

/// <summary>Each Business tenant's use past its contract in one calendar month (UTC), for the invoice.</summary>
/// <param name="MonthStart">The first day of the month reported.</param>
/// <param name="Closed">
/// True once the month is over and kept as it was then: its tenants, contracts and totals no longer change.
/// False for a month still running, or just ended, whose figures are worked out as they stand now.
/// </param>
public sealed record BusinessOverageReport(DateOnly MonthStart, IReadOnlyCollection<BusinessOverage> Tenants, bool Closed);

/// <summary>
/// One Business tenant's month against its contract (Docs/Billing.md, Business). Credits its credit packs
/// paid for are not overage. The amount is at the contract's rates: $1 per million extra messages and
/// $0.50 per 1,000 extra credits, rounded to the cent.
/// </summary>
/// <param name="CreditsUsed">Every credit its workspaces posted in the month, packs included.</param>
/// <param name="CreditsFromPacks">The part of <paramref name="CreditsUsed"/> drawn from its credit packs.</param>
public sealed record BusinessOverage(
    Guid TenantRefId,
    long MessagesPerMonth,
    long MessagesUsed,
    long ExtraMessages,
    decimal CreditsPerMonth,
    decimal CreditsUsed,
    decimal CreditsFromPacks,
    decimal ExtraCredits,
    decimal AmountUsd);
