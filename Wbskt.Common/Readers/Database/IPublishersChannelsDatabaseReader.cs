using Wbskt.Common.Records;

namespace Wbskt.Common.Readers.Database;

public interface IPublishersChannelsDatabaseReader
{
    IReadOnlyCollection<PublisherChannelReadRecord> GetAll(DateTime lastModified);
} 