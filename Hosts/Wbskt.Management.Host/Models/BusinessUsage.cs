using Wbskt.Models.Billing;

namespace Wbskt.Management.Host.Models;

/// <summary>A Business tenant's use in one month, as <c>dbo.BusinessUsage_GetBy_Month</c> reads it.</summary>
/// <param name="CreditsDrawn">The part of <paramref name="CreditsPosted"/> its credit packs paid for.</param>
public sealed record BusinessUsage(Guid TenantRefId, PlanLimitOverrides? LimitOverrides, long MessagesUsed, decimal CreditsPosted, decimal CreditsDrawn);
