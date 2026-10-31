namespace Wbskt.Management.Host.Models;

public sealed class ReadingsOptions
{
    public const string SectionName = "Readings";

    /// <summary>How long a reading is kept before the retention sweep deletes it.</summary>
    public int RetentionDays { get; set; } = 30;

    /// <summary>How often the retention sweep runs.</summary>
    public TimeSpan RetentionInterval { get; set; } = TimeSpan.FromHours(1);
}

/// <summary>One numeric state variable from a state report, as stored in the readings history.</summary>
public sealed record ClientReadingValue(string Name, double Value);

/// <summary>One stored reading, for export.</summary>
public sealed record ClientReading(string Name, DateTime DeviceTime, DateTime ReceivedAt, double Value, bool IsLate);
