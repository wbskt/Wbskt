using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;
using Wbskt.Models;

namespace Wbskt.Events.Billing;

/// <summary>
/// A paid account (Maker, Pro or Team) carried <c>Billing:PaidMessageThrottleMultiple</c> times its plan's
/// client messages this month, so its clients are now held to one message a minute each until the 1st.
/// Sent once per workspace and month by the management host's <c>MessageAllowanceMonitor</c>. A Free
/// account's throttle is announced by <see cref="WorkspaceMessageLimitReachedEvent"/> instead, since it
/// starts at the allowance.
/// </summary>
/// <param name="MessagesPerMonth">The plan's allowance.</param>
/// <param name="ThrottledAt">The messages a month at which the account was throttled.</param>
/// <param name="MessagesUsed">What the account had carried this month when it was found past that.</param>
[EventCriticality(EventCriticality.Warning)]
[SignalRNotify("OnWorkspaceMessagesThrottledEvent")]
public sealed record WorkspaceMessagesThrottledEvent(int WorkspaceId, long MessagesPerMonth, long ThrottledAt, long MessagesUsed) : BaseEvent, IWorkspaceContext;
