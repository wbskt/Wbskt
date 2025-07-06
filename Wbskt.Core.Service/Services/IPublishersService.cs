using Wbskt.Common.Records;

namespace Wbskt.Core.Service.Services;

public interface IPublishersService
{
    int Create(PublisherRecord publisherRecord);
    IReadOnlyCollection<PublisherReadRecord> GetAllForUser(int userId);
    int GetPublisherIdByRef(Guid publisherRef);
    Guid GetPublisherRefById(int publisherId);
    IReadOnlyCollection<int> GetPublisherIdsByRefs(Guid[] publisherRefs);
    IReadOnlyCollection<Guid> GetPublisherRefsByIds(int[] publisherIds);
} 