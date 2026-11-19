namespace Wbskt.Management.Host.Hubs;

/// <summary>
/// Pushed as <c>OnClientFeedSkipped</c> to a client's watchers when <see cref="ClientFeedThrottle"/>
/// dropped messages: <paramref name="Skipped"/> were not sent since the last report, at most about a second
/// before <paramref name="CreatedAtUtc"/>, which is the throttle's clock.
/// The comms history endpoint still has every one of them.
/// </summary>
public sealed record ClientFeedSkipped(Guid ClientRefId, int Skipped, DateTime CreatedAtUtc);
