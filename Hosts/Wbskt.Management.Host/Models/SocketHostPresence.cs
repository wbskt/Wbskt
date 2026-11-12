namespace Wbskt.Management.Host.Models;

/// <summary>
/// A client whose presence was cleared because the socket host holding it went silent (or restarted).
/// <c>ChangedAt</c> is the time the expiry recorded, when it recorded one; its announcement is dated by it.
/// </summary>
public sealed record ExpiredPresence(int ClientId, Guid ClientRefId, int WorkspaceId, string? HostId, DateTime? ChangedAt = null);

/// <summary>
/// A client whose presence a live socket host's snapshot repaired: set back online (<c>Connected</c>) or
/// cleared, on the snapshot's host. <c>ChangedAt</c> is the time recorded, which its announcement carries.
/// </summary>
public sealed record PresenceRepair(int ClientId, Guid ClientRefId, int WorkspaceId, bool Connected, DateTime ChangedAt);

/// <summary>How the management host decides a socket host has gone (see <c>StalePresenceSweeper</c>).</summary>
public sealed class PresenceOptions
{
    public const string SectionName = "Presence";

    /// <summary>
    /// A socket host not heard from for this long is treated as gone, and the clients it held as
    /// disconnected. Several times the socket host's heartbeat interval (30 s by default), so a late
    /// beat or two never expires a live host.
    /// </summary>
    public int HostStaleAfterSeconds { get; set; } = 120;

    /// <summary>How often the management host looks for presence held by a silent host.</summary>
    public int SweepIntervalSeconds { get; set; } = 60;
}
