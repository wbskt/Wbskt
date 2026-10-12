namespace Wbskt.Management.Models;

/// <summary>A summary of one variable's readings over one bucket of time.</summary>
public sealed record ClientReadingBucket(DateTime Start, long Count, double Min, double Avg, double Max);

/// <summary>
/// A variable's history over [<see cref="From"/>, <see cref="To"/>), in buckets of
/// <see cref="BucketSeconds"/>. Buckets with no readings are left out.
/// </summary>
public sealed record ClientReadingsResponse(
    string Name,
    DateTime From,
    DateTime To,
    int BucketSeconds,
    IReadOnlyCollection<ClientReadingBucket> Buckets);
