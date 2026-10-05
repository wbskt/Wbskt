namespace Wbskt.Events;

/// <summary>
/// Marks an event as raw device traffic (a message, a reply, a reading change) rather than an audit
/// record of something a person or the platform did. The management host's event log keeps these
/// for <c>EventLogging:DeviceTrafficRetentionDays</c>, a much shorter window than the audit log,
/// because they arrive at the rate devices talk; their numeric history lives in dbo.ClientReadings.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class DeviceTrafficAttribute : Attribute
{
    /// <summary>The names (as the event log stores them) of every event marked as device traffic.</summary>
    public static IReadOnlyList<string> EventNames { get; } = typeof(DeviceTrafficAttribute).Assembly.GetTypes()
        .Where(t => t.IsDefined(typeof(DeviceTrafficAttribute), inherit: false))
        .Select(t => t.Name)
        .OrderBy(n => n, StringComparer.Ordinal)
        .ToArray();
}
