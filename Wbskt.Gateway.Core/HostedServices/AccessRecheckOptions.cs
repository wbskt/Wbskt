namespace Wbskt.Gateway.Core.HostedServices;

/// <summary>The periodic re-check of held connections against the client token cutoffs (<c>AccessRecheck</c>).</summary>
public sealed class AccessRecheckOptions
{
    public const string SectionName = "AccessRecheck";

    public static readonly TimeSpan DefaultInterval = TimeSpan.FromMinutes(1);

    /// <summary>
    /// The longest interval taken. A cutoff in Redis lasts about an hour after the revoke, so a check
    /// must come well within that to see it.
    /// </summary>
    public static readonly TimeSpan MaxInterval = TimeSpan.FromMinutes(15);

    /// <summary>
    /// How often every held connection is checked. It bounds how long a client stays connected after a
    /// revoke, delete or secret rotation whose event this host missed. A value that is not positive, or
    /// is over <see cref="MaxInterval"/>, falls back to the default.
    /// </summary>
    public TimeSpan Interval { get; set; } = DefaultInterval;

    internal TimeSpan EffectiveInterval => Interval > TimeSpan.Zero && Interval <= MaxInterval ? Interval : DefaultInterval;
}
