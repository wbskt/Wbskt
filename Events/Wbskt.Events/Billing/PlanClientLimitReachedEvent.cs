using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;
using Wbskt.Models;

namespace Wbskt.Events.Billing;

/// <summary>
/// A client was refused because the account already has as many registered clients as its plan allows,
/// pooled across its workspaces: a board registering through an auto-approve policy, or someone approving
/// a pending or revoked client. The registration or approval answered 409 <c>PLAN_CLIENT_LIMIT_REACHED</c>;
/// this puts it in the policy's activity and on the console, where a board that is merely not connecting
/// would otherwise leave no trace.
/// </summary>
/// <param name="ClientRefId">The client being approved; empty for a registration, which creates none.</param>
/// <param name="ClientName">The name the board registered with; null for an approval.</param>
/// <param name="Current">Registered clients across the account when it was refused.</param>
[EventCriticality(EventCriticality.Warning)]
[SignalRNotify("OnPlanClientLimitReachedEvent")]
public sealed record PlanClientLimitReachedEvent(
    Guid PolicyRefId,
    int PolicyId,
    int WorkspaceId,
    string PlanCode,
    int Limit,
    int Current,
    Guid ClientRefId,
    string? ClientName)
    : BaseEvent, IPolicyContext;
