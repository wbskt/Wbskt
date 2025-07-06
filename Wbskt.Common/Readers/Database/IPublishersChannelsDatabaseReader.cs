using Wbskt.Common.Records;

namespace Wbskt.Common.Readers.Database;

internal interface IPublishersChannelsDatabaseReader
{
    IReadOnlyCollection<PublisherChannelReadRecord> GetAll(DateTime lastModified);
}
