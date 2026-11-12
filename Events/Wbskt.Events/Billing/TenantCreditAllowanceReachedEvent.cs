using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;
using Wbskt.Models;

namespace Wbskt.Events.Billing;

/// <summary>
/// A tenant's workspaces, together, just spent <see cref="Percent"/> (80 or 100) of the workflow credits its
/// plan includes this month. Past 100 its runs draw on its credit packs. Sent by the engine once the credits
/// that reach it are posted (<c>CreditUsagePoster</c>), and again only if the publish was not recorded; the
/// auth host emails the tenant's billing admins once a month for each. Logged in the tenant's workspace
/// that posted the most this month.
/// </summary>
/// <param name="CreditsPerMonth">The plan's allowance when it was crossed.</param>
/// <param name="CreditsSpent">What the tenant had posted this month when the notice was sent.</param>
[EventCriticality(EventCriticality.Warning)]
public sealed record TenantCreditAllowanceReachedEvent(int WorkspaceId, int TenantId, int Percent, decimal CreditsPerMonth, decimal CreditsSpent)
    : BaseEvent, IWorkspaceContext;
