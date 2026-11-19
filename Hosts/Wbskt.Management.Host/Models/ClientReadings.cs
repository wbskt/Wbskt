using Wbskt.Models.Billing;

namespace Wbskt.Management.Host.Models;

public sealed class ReadingsOptions
{
    public const string SectionName = "Readings";

    /// <summary>How often the retention sweep runs. How long readings are kept is the plan's (RawReadingsDays, HourlyReadingsDays).</summary>
    public TimeSpan RetentionInterval { get; set; } = TimeSpan.FromHours(1);
}

/// <summary>How long one plan keeps readings: raw, and rolled up per hour.</summary>
public sealed record ReadingRetention(PlanCode PlanCode, int RawDays, int HourlyDays);

/// <summary>
/// What the readings retention sweep did: raw rows rolled up, hourly rows deleted, deleted clients' rows
/// cleared. <see cref="More"/> when the last batch still came back full, so there is more to do.
/// </summary>
public sealed record ReadingCompaction(int RolledUp, int HourlyDeleted, int Cleared, bool More = false)
{
    public int Total => RolledUp + HourlyDeleted + Cleared;
}

/// <summary>One numeric state variable from a state report, as stored in the readings history.</summary>
public sealed record ClientReadingValue(string Name, double Value);

/// <summary>One stored reading, for export.</summary>
public sealed record ClientReading(string Name, DateTime SentAt, DateTime ReceivedAt, double Value, bool IsLate);
