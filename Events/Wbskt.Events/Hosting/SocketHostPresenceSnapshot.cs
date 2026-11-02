namespace Wbskt.Events.Hosting;

/// <summary>
/// Sent by every socket host every few minutes: the devices it holds at <see cref="TakenAtUtc"/>. The
/// management host clears the presence of any client it has on this host that connected before the
/// snapshot but is not in it, because its disconnect was lost (a failed publish, a parked event). The
/// heartbeat lease only catches a host that went silent; this catches a live one that lost an event.
/// </summary>
/// <remarks>Not an <c>IEvent</c>, for the same reason as <see cref="SocketHostHeartbeat"/>.</remarks>
public sealed record SocketHostPresenceSnapshot(string HostId, IReadOnlyList<Guid> ClientRefIds, DateTime TakenAtUtc);
