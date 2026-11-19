using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;
using Wbskt.Models;

namespace Wbskt.Events.Billing;

/// <summary>
/// A tenant's workspaces, together, just spent <see cref="Percent"/> (80 or 100) of the workflow credits its
/// plan includes this month. Past 100 its runs draw on its credit packs. Sent by the engine once the credits
/// that reach it are posted (<c>CreditUsagePoster</c>), and again only if the publish was not recorded; the
/// auth host emails the tenant's billing admins once a month for each. Logged in the tenant's workspace
/// that posted the most this month, and pushed to the console of every workspace of the tenant with only
/// its percent and month: the pooled figures are the account's, which a member without billing.read may
/// not see.
/// </summary>
/// <param name="CreditsPerMonth">The plan's allowance when it was crossed.</param>
/// <param name="CreditsSpent">What the tenant had posted that month when the notice was sent.</param>
/// <param name="MonthStart">
/// The month the allowance was reached in. Usually the month the event is sent in; the month before when
/// the crossing was posted just before it ended and announced after. Default on an event sent before it was
/// carried, which means the month of <see cref="BaseEvent.CreatedAtUtc"/>.
/// </param>
[EventCriticality(EventCriticality.Warning)]
[SignalRNotify("OnTenantCreditAllowanceReachedEvent", Scope = NotifyScope.Tenant)]
public sealed record TenantCreditAllowanceReachedEvent(
    int WorkspaceId,
    int TenantId,
    int Percent,
    [property: SignalRWithheld] decimal CreditsPerMonth,
    [property: SignalRWithheld] decimal CreditsSpent,
    DateOnly MonthStart = default)
    : BaseEvent, IWorkspaceContext, ITenantContext;
