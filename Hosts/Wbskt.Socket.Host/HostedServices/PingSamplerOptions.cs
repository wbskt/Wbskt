namespace Wbskt.Socket.Host.HostedServices;

/// <summary>The background latency sampler (<c>PingSampler</c>).</summary>
public sealed class PingSamplerOptions
{
    public const string SectionName = "PingSampler";

    public static readonly TimeSpan DefaultInterval = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How often every connected device is pinged. Each round is one ping per device, so this is kept
    /// long; the console's Ping button takes a reading on demand. A zero or negative value falls back
    /// to the default.
    /// </summary>
    public TimeSpan Interval { get; set; } = DefaultInterval;
}
