using Wbskt.Common.Records;

namespace Wbskt.Common.Readers.Database;

internal interface IPublishersDatabaseReader
{
    IReadOnlyCollection<PublisherReadRecord> GetAll(DateTime lastModified);

    IReadOnlyCollection<PublisherReadRecord> GetAllByUserId(DateTime lastModified, int userId);
}
