namespace Wbskt.Management.Models;

/// <summary>
/// The client messages a workspace carried over [<see cref="From"/>, <see cref="To"/>), in UTC buckets of
/// <see cref="BucketSeconds"/> (an hour or a day). Every bucket in the range is listed, oldest first,
/// with zero for one that carried nothing. <see cref="From"/> and <see cref="To"/> are the range asked
/// for, widened to whole buckets.
/// </summary>
public sealed record WorkspaceMessageHistory(
    DateTimeOffset From,
    DateTimeOffset To,
    int BucketSeconds,
    IReadOnlyCollection<MessageCountBucket> Buckets);

/// <summary>Messages clients sent (<see cref="Inbound"/>) and commands delivered to them (<see cref="Outbound"/>) in the bucket starting at <see cref="Start"/>.</summary>
public sealed record MessageCountBucket(DateTimeOffset Start, long Inbound, long Outbound);
