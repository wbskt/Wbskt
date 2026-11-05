namespace Wbskt.Events;

/// <summary>
/// Marks an event as raw client traffic (a message, a reply, a reading change) rather than an audit
/// record of something a person or the platform did. The management host's event log keeps these
/// for <c>EventLogging:ClientTrafficRetentionDays</c>, a much shorter window than the audit log,
/// because they arrive at the rate clients talk; their numeric history lives in dbo.ClientReadings.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ClientTrafficAttribute : Attribute
{
    /// <summary>The names (as the event log stores them) of every event marked as client traffic.</summary>
    public static IReadOnlyList<string> EventNames { get; } = typeof(ClientTrafficAttribute).Assembly.GetTypes()
        .Where(t => t.IsDefined(typeof(ClientTrafficAttribute), inherit: false))
        .Select(t => t.Name)
        .OrderBy(n => n, StringComparer.Ordinal)
        .ToArray();
}
