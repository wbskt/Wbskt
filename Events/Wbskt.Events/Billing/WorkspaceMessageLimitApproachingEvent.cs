using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;
using Wbskt.Models;

namespace Wbskt.Events.Billing;

/// <summary>
/// The workspace's account has carried 80% of its plan's client messages for the month. Sent once per
/// workspace and month by the management host's <c>MessageAllowanceMonitor</c>, and not at all when the
/// account went past its whole allowance between two checks (that is <see cref="WorkspaceMessageLimitReachedEvent"/>).
/// </summary>
/// <param name="MessagesPerMonth">The plan's allowance when the warning was raised.</param>
/// <param name="MessagesUsed">What the account had carried this month when it was found past 80%.</param>
[EventCriticality(EventCriticality.Warning)]
[SignalRNotify("OnWorkspaceMessageLimitApproachingEvent")]
public sealed record WorkspaceMessageLimitApproachingEvent(int WorkspaceId, long MessagesPerMonth, long MessagesUsed) : BaseEvent, IWorkspaceContext;
