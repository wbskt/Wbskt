using Wbskt.Common.Records;

namespace Wbskt.Common.Readers;

public interface IPublishersReader
{
    PublisherReadRecord GetById(int id);
    IReadOnlyCollection<PublisherReadRecord> GetAll();
    IReadOnlyCollection<PublisherReadRecord> GetAllByUserId(int userId);
}
