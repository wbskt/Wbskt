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
/// <param name="CreditsSpent">What the ledger held for the workspace this month when the notice was sent.</param>
[EventCriticality(EventCriticality.Warning)]
[SignalRNotify("OnWorkspaceCreditCapReachedEvent")]
public sealed record WorkspaceCreditCapReachedEvent(int WorkspaceId, int Percent, decimal MonthlyCreditCap, decimal CreditsSpent) : BaseEvent, IWorkspaceContext;
