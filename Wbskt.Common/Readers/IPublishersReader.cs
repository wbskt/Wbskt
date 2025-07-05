using Wbskt.Common.Records;

namespace Wbskt.Common.Readers;

internal interface IPublishersReader
{
    PublisherReadRecord GetById(int id);
    IReadOnlyCollection<PublisherReadRecord> GetAll();
    IReadOnlyCollection<PublisherReadRecord> GetAllByUserId(int userId);
}
