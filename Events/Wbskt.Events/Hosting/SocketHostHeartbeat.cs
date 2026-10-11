namespace Wbskt.Events.Hosting;

/// <summary>
/// Sent by every socket host on a fixed interval to say it is alive. The management host records the
/// time it last heard from each host, and clears the presence of devices held by a host it has not
/// heard from in a while: one that crashed, was replaced by a deploy (a new container is a new
/// host id) or was scaled away, none of which ever publishes the disconnects.
/// </summary>
/// <remarks>
/// Deliberately not an <c>IEvent</c>: the audit log consumes every <c>IEvent</c>, and a liveness
/// beat every few seconds per host is not something that happened to anyone.
/// </remarks>
public sealed record SocketHostHeartbeat(string HostId);
