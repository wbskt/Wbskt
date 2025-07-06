namespace Wbskt.Common.Records;

public record PublisherChannelRecord
{
    public int PublisherId { get; init; }
    public int ChannelId { get; init; }
    public DateTime LastModified { get; init; }
    public bool Deleted { get; init; }
} 