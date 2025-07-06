using Wbskt.Common.Records;

namespace Wbskt.Common.Readers;

public interface IPublishersReader
{
    PublisherReadRecord GetById(int id);
    PublisherReadRecord GetByRef(Guid publisherRef);
    IReadOnlyCollection<PublisherReadRecord> GetAll();
    IReadOnlyCollection<PublisherReadRecord> GetAllByUserId(int userId);
    IReadOnlyCollection<PublisherReadRecord> GetAllByIds(int[] ids);
    IReadOnlyCollection<PublisherReadRecord> GetAllByRefs(Guid[] publisherRefs);
}
