using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;
using Wbskt.Models;

namespace Wbskt.Events.Billing;

/// <summary>
/// A workspace's spend this month just reached <see cref="Percent"/> (80 or 100) of its monthly credit
/// cap. At 100 its new runs are refused until the 1st or until the cap is raised. Sent once per
/// threshold and month once the credits that reach it are posted (<c>CreditUsagePoster</c>).
/// </summary>
/// <param name="Percent">80 or 100.</param>
/// <param name="MonthlyCreditCap">The cap when it was crossed.</param>
/// <param name="CreditsSpent">What the ledger held for the workspace that month when the notice was sent.</param>
/// <param name="MonthStart">
/// The month the cap was reached in; the month before the one it is sent in when the crossing was posted
/// just before that month ended. Default on an event sent before it was carried, which means the month of
/// <see cref="BaseEvent.CreatedAtUtc"/>.
/// </param>
[EventCriticality(EventCriticality.Warning)]
[SignalRNotify("OnWorkspaceCreditCapReachedEvent")]
public sealed record WorkspaceCreditCapReachedEvent(int WorkspaceId, int Percent, decimal MonthlyCreditCap, decimal CreditsSpent, DateOnly MonthStart = default) : BaseEvent, IWorkspaceContext;
