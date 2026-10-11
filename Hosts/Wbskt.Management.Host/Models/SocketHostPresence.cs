namespace Wbskt.Management.Host.Models;

/// <summary>A client whose presence was cleared because the socket host holding it went silent.</summary>
public sealed record ExpiredPresence(int ClientId, Guid ClientRefId, int WorkspaceId, string? HostId);

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
