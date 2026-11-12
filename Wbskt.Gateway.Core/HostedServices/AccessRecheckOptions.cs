namespace Wbskt.Gateway.Core.HostedServices;

/// <summary>The periodic re-check of held connections against the client token cutoffs (<c>AccessRecheck</c>).</summary>
public sealed class AccessRecheckOptions
{
    public const string SectionName = "AccessRecheck";

    public static readonly TimeSpan DefaultInterval = TimeSpan.FromMinutes(1);

    /// <summary>
    /// How often every held connection is checked. It bounds how long a client stays connected after a
    /// revoke, delete or secret rotation whose event this host missed. A zero or negative value falls
    /// back to the default.
    /// </summary>
    public TimeSpan Interval { get; set; } = DefaultInterval;
}
