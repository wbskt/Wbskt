using Wbskt.Common.Records;

namespace Wbskt.Common.Writers;

public interface IPublishersWriter
{
    int InsertPublisher(PublisherRecord record);
}
