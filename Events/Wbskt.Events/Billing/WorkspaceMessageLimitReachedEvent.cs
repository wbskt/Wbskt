using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;
using Wbskt.Models;

namespace Wbskt.Events.Billing;

/// <summary>
/// The workspace's account has carried its plan's client messages for the month. On Free its clients
/// are now held to one message a minute each until the 1st; on a paid plan what follows is overage.
/// Sent once per workspace and month by the management host's <c>MessageAllowanceMonitor</c>, within
/// about a minute of the crossing.
/// </summary>
/// <param name="MessagesPerMonth">The plan's allowance when it was reached.</param>
/// <param name="MessagesUsed">What the account had carried this month when it was found over it.</param>
/// <param name="Throttled">Whether the account's clients are now throttled (Free).</param>
[EventCriticality(EventCriticality.Warning)]
[SignalRNotify("OnWorkspaceMessageLimitReachedEvent")]
public sealed record WorkspaceMessageLimitReachedEvent(int WorkspaceId, long MessagesPerMonth, long MessagesUsed, bool Throttled) : BaseEvent, IWorkspaceContext;
