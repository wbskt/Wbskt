namespace Wbskt.Management.Host.Hubs;

/// <summary>
/// Pushed as <c>OnClientFeedSkipped</c> to a device's watchers when <see cref="ClientFeedThrottle"/>
/// dropped messages: <paramref name="Skipped"/> were not sent in the second before <paramref name="CreatedAtUtc"/>.
/// The comms history endpoint still has every one of them.
/// </summary>
public sealed record ClientFeedSkipped(Guid ClientRefId, int Skipped, DateTime CreatedAtUtc);
