namespace Wbskt.Management.Models;

/// <summary>
/// Client traffic across a workspace, averaged over the <see cref="WindowSeconds"/> before
/// <see cref="CreatedAtUtc"/>. Inbound is messages clients sent; outbound is commands written to a
/// client's connection. Pushed as <c>OnWorkspaceMessageRateEvent</c> to the workspace group while the
/// rate is non-zero, and once more when it drops to zero.
/// </summary>
public sealed record WorkspaceMessageRate(
    double InboundPerSecond,
    double OutboundPerSecond,
    double MessagesPerSecond,
    int WindowSeconds,
    DateTimeOffset CreatedAtUtc);
