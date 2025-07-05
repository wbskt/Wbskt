using Wbskt.Common.Records;

namespace Wbskt.Core.Service.Services;

public interface IPublisherService
{
    int Create(PublisherRecord publisherRecord);

    IReadOnlyCollection<PublisherReadRecord> GetAllForUser(int userId);
}
