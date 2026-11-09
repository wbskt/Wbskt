namespace Wbskt.Events.Billing;

/// <summary>
/// Sent by the management host every minute: the workspaces whose account has carried as many messages
/// this month as its plan allows before throttling (Free its allowance, Maker and Pro a multiple of it),
/// so every socket host holds their clients to one message a minute. The whole list each time, so a host
/// that starts or missed one is right by the next, and a workspace drops off it on the 1st or once its
/// account moves to a larger plan.
/// </summary>
/// <remarks>Not an <c>IEvent</c>, as <c>SocketHostHeartbeat</c> is not: a state broadcast every minute is not something that happened.</remarks>
public sealed record MessageThrottleSnapshot(IReadOnlyList<int> WorkspaceIds, DateTime TakenAtUtc);
